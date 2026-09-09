# Clasificación de Tareas — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar a `Tarea` los cinco clasificadores opcionales (Zona, Dimensión temática, Organismo responsable, Origen de financiamiento, Expediente), la reclasificación restringida a Admin, y la sección "Tareas vinculadas" en la ficha de documento.

**Architecture:** Cinco columnas nullable nuevas en `Tarea` (cuatro FKs a los catálogos del Plan A + FK a `DocumentoAdministrativo`), migración puramente aditiva. `TareaService` valida los cinco valores con la MISMA rutina tanto en `CrearAsync` como en `ReclasificarAsync` (D12), inyectando los repositorios de catálogo (no los servicios, para no acoplar la validación interna al permiso `catalogo.maestras`). `ReclasificarAsync` sigue el molde de `CambiarPrioridadAsync` (guard de "sin cambios", nota automática + auditoría) pero, a diferencia de esa acción, alcanza también a tareas terminales (D9). El vínculo con Documentos va en un solo sentido (D11): `Tarea` conoce a `DocumentoAdministrativo`, no al revés — la ficha del expediente pregunta vía `ITareaService.ListarPorDocumentoAsync`. En Presentation, un panel reusable (`ClasificacionTareaPanelViewModel`/`View`) se embebe tanto en el alta inline como en un modal nuevo de reclasificación (`ReclasificarTareaDialog`), primer diálogo con formulario real de la app (hasta hoy los diálogos solo pedían texto libre o sí/no).

**Tech Stack:** .NET 10, C#, EF Core 10 + Npgsql/PostgreSQL, ASP.NET Core Minimal API, Avalonia 12.0.5 + CommunityToolkit.Mvvm, xUnit (Postgres real para Infrastructure/Api, Moq en Application.Tests/Presentation.Tests, fakes escritos a mano en Presentation.UiTests).

**Spec:** docs/superpowers/specs/2026-09-08-tareas-clasificadores-design.md

**Depende de:** docs/superpowers/plans/2026-09-08-tareas-catalogos.md (debe estar completo antes de empezar este plan) — de ahí salen las entidades `Zona`/`DimensionTematica`/`OrganismoResponsable`/`OrigenFinanciamiento`, sus DbSets (`Zonas`/`DimensionesTematicas`/`OrganismosResponsables`/`OrigenesFinanciamiento`), y sus servicios `IZonaService`/`IDimensionTematicaService`/`IOrganismoResponsableService`/`IOrigenFinanciamientoService` (calcados de `ICategoriaService`).

## Global Constraints

- Rama de trabajo: crear `feat/tareas-clasificacion` desde `main` (que para entonces ya tiene el Plan A mergeado). No pushear.
- Commits: conventional commits en español, uno por tarea como mínimo. **NUNCA agregar "Co-Authored-By" ni atribución a IA.**
- TDD estricto: escribir el test, **correrlo y verlo fallar**, implementar lo mínimo, correrlo y verlo pasar, commitear.
- **NUNCA correr `StockApp.Application.Tests` y `StockApp.Api.Tests` en paralelo** (colisionan por Testcontainers/Postgres compartido). Siempre secuencial.
- `StockApp.Infrastructure.Tests` corre contra PostgreSQL real; el contenedor `stockapp-pg` tiene que estar levantado.
- `AccionAuditada` es **append-only**: nunca reordenar ni reutilizar valores. Antes de este plan (sin el Plan A todavía aplicado) el último valor del archivo era `EdicionDocumento = 59`. El Plan A, que corre ANTES, suma sus propios 12 valores para el ABM de los 4 catálogos (Alta/Modificación/BajaLógica × 4, de `AltaZona = 60` a `ModificacionOrigenFinanciamiento = 71` — confirmado leyendo la Task 4 del plan de catálogos) inmediatamente después. Por eso la Task 3 de este plan da el código con el valor **72** (el siguiente libre UNA VEZ que el Plan A ya corrió, que es el orden real de ejecución — este plan depende de A). Aun así, la instrucción de esa task sigue siendo explícita: antes de escribirlo, abrí `AccionAuditada.cs` y usá el entero siguiente al último valor REALMENTE presente en el archivo en ese momento — nunca asumas 72 a ciegas, por si el Plan A terminó agregando una cantidad distinta de valores.
- Contrato de tipos del Plan A para los REPOSITORIOS (confirmado leyendo la Task 3 del plan de catálogos, que SÍ especifica entidades, servicios Y repositorios — no es una derivación asumida): `IZonaRepository`, `IDimensionTematicaRepository`, `IOrganismoResponsableRepository`, `IOrigenFinanciamientoRepository`, cada uno con `Task<T?> ObtenerPorIdAsync(int id)`, `Task<IReadOnlyList<T>> ListarTodasAsync()`, `Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)`, `Task<int> AgregarAsync(T entidad)`, `Task ActualizarAsync(T entidad)` — mismo molde que `ICategoriaRepository` (`src/StockApp.Application/Interfaces/ICategoriaRepository.cs`). `TareaService` inyecta estos repositorios (no los servicios) para validar existencia/actividad sin acoplar la reclasificación al permiso `catalogo.maestras`. Namespace: `StockApp.Application.Interfaces` — mismo namespace EXACTO que `ICategoriaRepository` (NO `StockApp.Application.Catalogo`, que es donde vive `ICategoriaService`, la interfaz de SERVICIO, no de repositorio). `TareaService.cs`/`TareaServiceTests.cs` ya tienen `using StockApp.Application.Interfaces;`, así que Task 4 no necesita agregar ningún `using` nuevo para estos 4 repositorios — confirmar el namespace real leyendo el archivo antes de ese paso, por si el Plan A decidió otra cosa.
- Las Views de Avalonia **no se auto-inicializan**: toda vista con datos async nuevos engancha `DataContextChanged`. `TareaFormView` hoy NO lo hace (comentario del archivo: "sin combos que precargar") — ese comentario deja de ser cierto en la Task 14 y se reemplaza.
- `UnauthorizedAccessException` en los ViewModels se captura por el mecanismo ya existente de cada pantalla (en `TareaFormViewModel` se traduce a `MensajeSinPermiso` vía `ResolverMensajeError`, NO en silencio — a diferencia de `DocumentoFormViewModel`). No cambiar ese criterio.
- Compiled bindings (`x:DataType`): un typo de binding en `.axaml` es error de BUILD, no null silencioso. Un comentario XML con `--` dentro de un `.axaml` rompe el build con AVLN1001.
- `dotnet build` con más de un `.csproj` en el mismo comando da falso verde (MSBuild descarta el segundo proyecto en silencio) — usar la `.sln` o un proyecto por vez.
- Spec de referencia: `docs/superpowers/specs/2026-09-08-tareas-clasificadores-design.md`

---

### Task 1: `Tarea` — cinco propiedades de clasificación + comentario de invariantes

**Files:**
- Modify: `src/StockApp.Domain/Entities/Tarea.cs`
- Test: `tests/StockApp.Domain.Tests/Entities/TareaTests.cs`

**Interfaces:**
- Consumes: `Zona`, `DimensionTematica`, `OrganismoResponsable`, `OrigenFinanciamiento` (Plan A, `StockApp.Domain.Entities`), `DocumentoAdministrativo` (`src/StockApp.Domain/Entities/DocumentoAdministrativo.cs`).
- Produces (consumido por Tasks 2-17): `Tarea.ZonaId/Zona`, `.DimensionTematicaId/DimensionTematica`, `.OrganismoResponsableId/OrganismoResponsable`, `.OrigenFinanciamientoId/OrigenFinanciamiento`, `.DocumentoAdministrativoId/DocumentoAdministrativo` — los cinco pares `int?`/nav nullable.

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Domain.Tests/Entities/TareaTests.cs` (al final de la clase `TareaTests`, antes del último `}`):

```csharp

    // ── Clasificadores (spec 2026-09-08): puramente aditivo, sin efecto en la máquina de
    // estados ni en CambiarPrioridad — estos tests solo custodian que la migración de datos
    // sea coherente con el dominio (los cinco campos nacen null, D7).

    [Fact]
    public void NuevaTarea_LosCincoClasificadoresNacenNulos()
    {
        var tarea = NuevaTarea();

        Assert.Null(tarea.ZonaId);
        Assert.Null(tarea.Zona);
        Assert.Null(tarea.DimensionTematicaId);
        Assert.Null(tarea.DimensionTematica);
        Assert.Null(tarea.OrganismoResponsableId);
        Assert.Null(tarea.OrganismoResponsable);
        Assert.Null(tarea.OrigenFinanciamientoId);
        Assert.Null(tarea.OrigenFinanciamiento);
        Assert.Null(tarea.DocumentoAdministrativoId);
        Assert.Null(tarea.DocumentoAdministrativo);
    }

    [Fact]
    public void CambiarEstado_TareaClasificada_NoTocaLosClasificadores()
    {
        // D9 del spec: reclasificar/clasificar es independiente de la máquina de estados —
        // este test prueba la mitad "estado no toca clasificación" (la otra mitad,
        // "reclasificar no toca estado", vive en TareaServiceTests, Task 5).
        var tarea = NuevaTarea(EstadoTarea.Pendiente);
        tarea.ZonaId = 3;
        tarea.OrigenFinanciamientoId = 7;

        tarea.CambiarEstado(EstadoTarea.EnCurso);

        Assert.Equal(3, tarea.ZonaId);
        Assert.Equal(7, tarea.OrigenFinanciamientoId);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Domain.Tests --filter "FullyQualifiedName~TareaTests.NuevaTarea_LosCincoClasificadoresNacenNulos"`
Expected: FAIL con error de compilación — `Tarea` no tiene una propiedad `ZonaId` (CS1061 o similar).

- [ ] **Step 3: Implementación mínima**

En `src/StockApp.Domain/Entities/Tarea.cs`, reemplazar el bloque de comentario XML del principio del archivo:

```csharp
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
```

por:

```csharp
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Domain.Entities;

/// <summary>
/// Tarea operativa del equipo (spec 2026-08-01). Lista común: se crea sin responsable,
/// cualquiera la toma (decisión 3). Sin baja lógica: Cancelada es un estado del ciclo de
/// vida, no un Activo=false (decisión 6). Guarda dos pares de trazabilidad independientes —
/// TomadaPor+FechaInicio (quién trabajó) y CerradaPor+FechaFin (quién cerró) — porque
/// cualquiera puede terminar o soltar una tarea ajena (decisión 11).
/// Clasificadores (spec 2026-09-08, D7/D20): desde ese spec YA NO es un módulo sin FK a
/// otras entidades del dominio — Zona/DimensionTematica/OrganismoResponsable/
/// OrigenFinanciamiento/DocumentoAdministrativo son cinco FKs opcionales, todas nullable y
/// sin backfill (migración puramente aditiva). Son puros datos de clasificación: no
/// participan de TransicionesValidas ni de CambiarPrioridad, y ReclasificarAsync
/// (TareaService) los reemplaza sin tocar Estado.
/// </summary>
public class Tarea
```

Agregar las cinco propiedades nuevas justo antes de `public List<NotaTarea> Notas { get; set; } = new();`:

```csharp
    public int? ZonaId { get; set; }
    public Zona? Zona { get; set; }
    public int? DimensionTematicaId { get; set; }
    public DimensionTematica? DimensionTematica { get; set; }
    public int? OrganismoResponsableId { get; set; }
    public OrganismoResponsable? OrganismoResponsable { get; set; }
    public int? OrigenFinanciamientoId { get; set; }
    public OrigenFinanciamiento? OrigenFinanciamiento { get; set; }
    public int? DocumentoAdministrativoId { get; set; }
    public DocumentoAdministrativo? DocumentoAdministrativo { get; set; }

    public List<NotaTarea> Notas { get; set; } = new();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Domain.Tests --filter "FullyQualifiedName~TareaTests"`
Expected: PASS (todos los tests de `TareaTests.cs`, incluidos los dos nuevos).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Domain/Entities/Tarea.cs tests/StockApp.Domain.Tests/Entities/TareaTests.cs
git commit -m "feat(tareas): agrega los cinco clasificadores opcionales a Tarea"
```

---

### Task 2: `AppDbContext` — mapeo EF de las cinco FKs + migración aditiva

**Files:**
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`
- Create (generado por el comando `dotnet ef`): `src/StockApp.Infrastructure/Migrations/<timestamp>_AgregaClasificadoresATarea.cs` y su `.Designer.cs`
- Modify (generado por el mismo comando): `src/StockApp.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`

**Interfaces:**
- Consumes: `Tarea` (Task 1), `Zona`/`DimensionTematica`/`OrganismoResponsable`/`OrigenFinanciamiento` (Plan A, ya con sus DbSets `Zonas`/`DimensionesTematicas`/`OrganismosResponsables`/`OrigenesFinanciamiento` registrados en este mismo archivo), `DocumentoAdministrativo` (DbSet `DocumentosAdministrativos`, ya existente).
- Produces: las 5 columnas nullable en la tabla `Tareas` con sus FKs `Restrict`, listas para Task 4 (repositorio).

Este task no tiene un test unitario propio — se verifica end-to-end en Task 4 (`TareaRepositoryTests`, contra Postgres real). Por eso los Steps van directo a la implementación y terminan con una inspección manual del archivo generado, mismo criterio que la migración de Documentos (`docs/superpowers/plans/2026-08-11-documentos-administrativos.md`, Task de migración).

- [ ] **Step 1: Mapear las cinco FKs en `OnModelCreating`**

En `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`, ubicar el bloque:

```csharp
        modelBuilder.Entity<Tarea>(e =>
        {
            e.Property(t => t.Titulo).IsRequired();
            e.HasIndex(t => t.Estado);
            e.HasOne(t => t.CreadaPor).WithMany()
                .HasForeignKey(t => t.CreadaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.TomadaPor).WithMany()
                .HasForeignKey(t => t.TomadaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.CerradaPor).WithMany()
                .HasForeignKey(t => t.CerradaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        });
```

y reemplazarlo por:

```csharp
        modelBuilder.Entity<Tarea>(e =>
        {
            e.Property(t => t.Titulo).IsRequired();
            e.HasIndex(t => t.Estado);
            e.HasOne(t => t.CreadaPor).WithMany()
                .HasForeignKey(t => t.CreadaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.TomadaPor).WithMany()
                .HasForeignKey(t => t.TomadaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.CerradaPor).WithMany()
                .HasForeignKey(t => t.CerradaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);

            // ── Clasificadores (spec 2026-09-08, D7) ──────────────────────────────
            // Las 5 FKs son nullable y Restrict: migración puramente aditiva, sin backfill,
            // ninguna tarea existente se toca. Los catálogos (Plan A) usan baja lógica
            // (Activo), nunca DELETE físico, así que Restrict nunca dispara en la práctica —
            // mismo criterio que CreadaPor/TomadaPor/CerradaPor arriba.
            e.HasOne(t => t.Zona).WithMany()
                .HasForeignKey(t => t.ZonaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.DimensionTematica).WithMany()
                .HasForeignKey(t => t.DimensionTematicaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.OrganismoResponsable).WithMany()
                .HasForeignKey(t => t.OrganismoResponsableId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.OrigenFinanciamiento).WithMany()
                .HasForeignKey(t => t.OrigenFinanciamientoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.DocumentoAdministrativo).WithMany()
                .HasForeignKey(t => t.DocumentoAdministrativoId).OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 2: Generar la migración**

Run: `dotnet ef migrations add AgregaClasificadoresATarea --project src/StockApp.Infrastructure --startup-project src/StockApp.Api`

Inspeccionar el archivo generado en `src/StockApp.Infrastructure/Migrations/<timestamp>_AgregaClasificadoresATarea.cs`: confirmar que agrega exactamente 5 columnas nullable (`ZonaId`, `DimensionTematicaId`, `OrganismoResponsableId`, `OrigenFinanciamientoId`, `DocumentoAdministrativoId`, todas `int`, `nullable: true`) a la tabla `Tareas`, sin tocar ninguna otra tabla, con 5 FKs `onDelete: ReferentialAction.Restrict`, y 5 índices simples (EF los crea por defecto sobre cada FK). No transcribir el contenido a mano — se generó con el comando.

- [ ] **Step 3: Aplicar la migración contra Postgres real y confirmar que no rompe nada existente**

Run: `dotnet ef database update --project src/StockApp.Infrastructure --startup-project src/StockApp.Api`
Expected: aplica sin error contra el contenedor `stockapp-pg`.

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~TareaRepositoryTests"`
Expected: PASS (la suite existente de Tareas, que corre contra la tabla ya migrada, sigue verde — todavía no usa las columnas nuevas).

- [ ] **Step 4: Commit**
```bash
git add src/StockApp.Infrastructure/Persistence/AppDbContext.cs src/StockApp.Infrastructure/Migrations/
git commit -m "feat(tareas): mapea las FKs de clasificación en EF y genera la migración aditiva"
```

---

### Task 3: `TareaRepository` — Includes completos + `ListarPorDocumentoAsync`

**Files:**
- Modify: `src/StockApp.Application/Interfaces/ITareaRepository.cs`
- Modify: `src/StockApp.Infrastructure/Repositories/TareaRepository.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/TareaRepositoryTests.cs`

**Interfaces:**
- Consumes: `Tarea` (Task 1, con las 5 navs), `AppDbContext.Tareas` (ya mapeado, Task 2).
- Produces: `ITareaRepository.ListarPorDocumentoAsync(int documentoId)`, y `ObtenerPorIdAsync`/`ListarAsync` ahora traen las 5 navs de clasificación cargadas (antes solo traían `TomadaPor`+`Notas`) — lo consume Task 5 (validación con nombres para el diff) y Task 16 (Presentation).

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Infrastructure.Tests/Repositories/TareaRepositoryTests.cs` (al final de la clase, antes del último `}`; reusa el helper privado `NuevoUsuario` ya existente en el archivo):

```csharp

    [Fact]
    public async Task ObtenerPorIdAsync_ConClasificacionCompleta_TraeLasCincoNavsCargadas()
    {
        Context.Usuarios.Add(NuevoUsuario());
        await Context.SaveChangesAsync();

        var zona = new Zona { Nombre = "Centro" };
        var dimension = new DimensionTematica { Nombre = "Tránsito" };
        var organismo = new OrganismoResponsable { Nombre = "Intendencia" };
        var origen = new OrigenFinanciamiento { Nombre = "Presupuesto propio" };
        var documento = new DocumentoAdministrativo
        {
            Numero = "0099", Anio = 2026, Tipo = TipoDocumento.Expediente,
            FechaEmision = DateTime.UtcNow.Date, Descripcion = "Expediente de prueba",
            RegistradoPorUsuarioId = 1, FechaRegistro = DateTime.UtcNow,
        };
        Context.Zonas.Add(zona);
        Context.DimensionesTematicas.Add(dimension);
        Context.OrganismosResponsables.Add(organismo);
        Context.OrigenesFinanciamiento.Add(origen);
        Context.DocumentosAdministrativos.Add(documento);
        await Context.SaveChangesAsync();

        var tarea = NuevaTarea();
        tarea.ZonaId = zona.Id;
        tarea.DimensionTematicaId = dimension.Id;
        tarea.OrganismoResponsableId = organismo.Id;
        tarea.OrigenFinanciamientoId = origen.Id;
        tarea.DocumentoAdministrativoId = documento.Id;
        var id = await _repo.AgregarAsync(tarea);

        var recuperada = await _repo.ObtenerPorIdAsync(id);

        Assert.Equal("Centro", recuperada!.Zona?.Nombre);
        Assert.Equal("Tránsito", recuperada.DimensionTematica?.Nombre);
        Assert.Equal("Intendencia", recuperada.OrganismoResponsable?.Nombre);
        Assert.Equal("Presupuesto propio", recuperada.OrigenFinanciamiento?.Nombre);
        Assert.Equal("0099", recuperada.DocumentoAdministrativo?.Numero);
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_DevuelveSoloLasTareasVinculadasAEseDocumento()
    {
        Context.Usuarios.Add(NuevoUsuario());
        await Context.SaveChangesAsync();

        var documento = new DocumentoAdministrativo
        {
            Numero = "0050", Anio = 2026, Tipo = TipoDocumento.Expediente,
            FechaEmision = DateTime.UtcNow.Date, Descripcion = "Expediente vinculado",
            RegistradoPorUsuarioId = 1, FechaRegistro = DateTime.UtcNow,
        };
        Context.DocumentosAdministrativos.Add(documento);
        await Context.SaveChangesAsync();

        var vinculada = NuevaTarea("Vinculada al expediente");
        vinculada.DocumentoAdministrativoId = documento.Id;
        await _repo.AgregarAsync(vinculada);

        var suelta = NuevaTarea("Sin expediente");
        await _repo.AgregarAsync(suelta);

        var resultado = await _repo.ListarPorDocumentoAsync(documento.Id);

        var fila = Assert.Single(resultado);
        Assert.Equal("Vinculada al expediente", fila.Titulo);
    }
```

`DocumentoAdministrativo`/`TipoDocumento` ya resuelven vía `using StockApp.Domain.Entities;`/`using StockApp.Domain.Enums;`, ya presentes en el archivo — no hace falta ningún `using` nuevo para este test.

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~TareaRepositoryTests.ListarPorDocumentoAsync_DevuelveSoloLasTareasVinculadasAEseDocumento"`
Expected: FAIL con error de compilación — `ITareaRepository`/`TareaRepository` no tienen `ListarPorDocumentoAsync`.

- [ ] **Step 3: Implementación mínima**

En `src/StockApp.Application/Interfaces/ITareaRepository.cs`, reemplazar:

```csharp
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

por:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface ITareaRepository
{
    Task<int> AgregarAsync(Tarea tarea);
    Task<Tarea?> ObtenerPorIdAsync(int id);

    /// <summary>Todas las tareas, sin filtrar por usuario (decisión 10 del spec).</summary>
    Task<IReadOnlyList<Tarea>> ListarAsync();

    /// <summary>Tareas vinculadas a un expediente (spec 2026-09-08, D11/D13): la dependencia
    /// va en un solo sentido (Tarea conoce a DocumentoAdministrativo), así que la ficha del
    /// documento pregunta acá en vez de que Documentos cargue una colección de tareas.</summary>
    Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId);

    /// <summary><paramref name="tarea"/> debe ser la instancia tracked de ObtenerPorIdAsync.</summary>
    Task ActualizarAsync(Tarea tarea);
}
```

En `src/StockApp.Infrastructure/Repositories/TareaRepository.cs`, reemplazar:

```csharp
    private IQueryable<Tarea> ConIncludes() =>
        _ctx.Tareas
            .Include(t => t.TomadaPor)
            .Include(t => t.Notas.OrderBy(n => n.Fecha).ThenBy(n => n.Id));
```

por:

```csharp
    // Clasificadores (spec 2026-09-08): las 5 navs se cargan siempre, igual que TomadaPor —
    // Api/TareasEndpoints.ADto (Task 6) y Presentation (Tasks 12-16) necesitan los nombres,
    // no solo los ids.
    private IQueryable<Tarea> ConIncludes() =>
        _ctx.Tareas
            .Include(t => t.TomadaPor)
            .Include(t => t.Zona)
            .Include(t => t.DimensionTematica)
            .Include(t => t.OrganismoResponsable)
            .Include(t => t.OrigenFinanciamiento)
            .Include(t => t.DocumentoAdministrativo)
            .Include(t => t.Notas.OrderBy(n => n.Fecha).ThenBy(n => n.Id));
```

y agregar el método nuevo después de `ListarAsync`:

```csharp
    public async Task<IReadOnlyList<Tarea>> ListarAsync()
        => await ConIncludes().OrderByDescending(t => t.FechaCreacion).ToListAsync();

    public async Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId)
        => await ConIncludes()
            .Where(t => t.DocumentoAdministrativoId == documentoId)
            .OrderByDescending(t => t.FechaCreacion)
            .ToListAsync();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~TareaRepositoryTests"`
Expected: PASS (toda la clase, incluidos los dos tests nuevos).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Interfaces/ITareaRepository.cs src/StockApp.Infrastructure/Repositories/TareaRepository.cs tests/StockApp.Infrastructure.Tests/Repositories/TareaRepositoryTests.cs
git commit -m "feat(tareas): TareaRepository trae las navs de clasificación y suma ListarPorDocumentoAsync"
```

---

### Task 4: `TareaService` — `CrearAsync` valida los cinco clasificadores (D12)

**Files:**
- Modify: `src/StockApp.Application/Tareas/TareaService.cs`
- Test: `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs`

**Interfaces:**
- Consumes: `IZonaRepository`/`IDimensionTematicaRepository`/`IOrganismoResponsableRepository`/`IOrigenFinanciamientoRepository` (contrato asumido del Plan A, ver Global Constraints), `IDocumentoAdministrativoRepository` (`src/StockApp.Application/Interfaces/IDocumentoAdministrativoRepository.cs`, ya existente).
- Produces: `TareaService` con constructor extendido (5 repos nuevos) y un helper privado `ValidarClasificacionAsync` reutilizado por Task 5 (`ReclasificarAsync`).

Este task rompe el constructor de `TareaService` — el `Crear()` helper de `TareaServiceTests.cs` es el ÚNICO sitio de construcción en ese archivo (confirmado por lectura directa), así que el fixup es de un solo lugar.

- [ ] **Step 1: Escribir el test que falla**

Reemplazar el helper `Crear` de `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs`:

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
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), It.IsAny<string>()));

        // Fix (review final, Critical): NotaAjenaAsync ahora resuelve el nombre del actor
        // contra IUsuarioRepository (igual que el tomador) en vez de leerlo de la sesión —
        // HttpCurrentSession (el ICurrentSession real de la API) siempre lo trae vacío. Este
        // mock del actor mantiene "garcia"/"admin.test" disponibles para los tests de nota
        // automática que ya existían, sin cambiar ninguna aserción.
        usuarios.Setup(u => u.ObtenerPorIdAsync(idSesion))
            .ReturnsAsync(new Usuario { Id = idSesion, NombreUsuario = nombreUsuario });

        var svc = new TareaService(repo.Object, usuarios.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, usuarios, session, auth, audit);
    }
```

por:

```csharp
    private static (TareaService Svc, Mock<ITareaRepository> Repo, Mock<IUsuarioRepository> Usuarios,
                     Mock<ICurrentSession> Session, Mock<IAuthorizationService> Auth, Mock<IAuditLogger> Audit,
                     Mock<IZonaRepository> Zonas, Mock<IDimensionTematicaRepository> Dimensiones,
                     Mock<IOrganismoResponsableRepository> Organismos, Mock<IOrigenFinanciamientoRepository> Origenes,
                     Mock<IDocumentoAdministrativoRepository> Documentos)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1, string nombreUsuario = "admin.test")
    {
        var repo        = new Mock<ITareaRepository>();
        var usuarios    = new Mock<IUsuarioRepository>();
        var session     = new Mock<ICurrentSession>();
        var auth        = new Mock<IAuthorizationService>();
        var audit       = new Mock<IAuditLogger>();
        var zonas       = new Mock<IZonaRepository>();
        var dimensiones = new Mock<IDimensionTematicaRepository>();
        var organismos  = new Mock<IOrganismoResponsableRepository>();
        var origenes    = new Mock<IOrigenFinanciamientoRepository>();
        var documentos  = new Mock<IDocumentoAdministrativoRepository>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(idSesion, nombreUsuario, rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), It.IsAny<string>()));

        // Fix (review final, Critical): NotaAjenaAsync ahora resuelve el nombre del actor
        // contra IUsuarioRepository (igual que el tomador) en vez de leerlo de la sesión —
        // HttpCurrentSession (el ICurrentSession real de la API) siempre lo trae vacío. Este
        // mock del actor mantiene "garcia"/"admin.test" disponibles para los tests de nota
        // automática que ya existían, sin cambiar ninguna aserción.
        usuarios.Setup(u => u.ObtenerPorIdAsync(idSesion))
            .ReturnsAsync(new Usuario { Id = idSesion, NombreUsuario = nombreUsuario });

        var svc = new TareaService(
            repo.Object, usuarios.Object, session.Object, auth.Object, audit.Object,
            zonas.Object, dimensiones.Object, organismos.Object, origenes.Object, documentos.Object);
        return (svc, repo, usuarios, session, auth, audit, zonas, dimensiones, organismos, origenes, documentos);
    }
```

`IZonaRepository`/`IDimensionTematicaRepository`/`IOrganismoResponsableRepository`/`IOrigenFinanciamientoRepository` viven en `StockApp.Application.Interfaces` (ver Global Constraints) — `TareaServiceTests.cs` ya tiene `using StockApp.Application.Interfaces;`, así que no hace falta agregar ningún `using` nuevo. `Zona`/`DimensionTematica`/`OrganismoResponsable`/`OrigenFinanciamiento` (las ENTIDADES, usadas en los `new Zona {...}` de los tests de abajo) viven en `StockApp.Domain.Entities`, también ya importado. Confirmar ambos namespaces reales antes de este paso, por si el Plan A decidió otra cosa.

Agregar al final de la clase `TareaServiceTests` (antes del último `}`):

```csharp

    // ── CrearAsync: validación de clasificadores (D12 del spec) ────────────────

    [Fact]
    public async Task CrearAsync_ZonaInexistente_LanzaReglaDeNegocioSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(99)).ReturnsAsync((Zona?)null);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.CrearAsync(new Tarea { Titulo = "x", ZonaId = 99 }));

        ctx.Repo.Verify(r => r.AgregarAsync(It.IsAny<Tarea>()), Times.Never);
    }

    [Fact]
    public async Task CrearAsync_ZonaInactiva_LanzaReglaDeNegocio()
    {
        var ctx = Crear();
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(5)).ReturnsAsync(new Zona { Id = 5, Nombre = "Centro", Activo = false });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.CrearAsync(new Tarea { Titulo = "x", ZonaId = 5 }));
    }

    [Fact]
    public async Task CrearAsync_DocumentoNoEsExpediente_LanzaReglaDeNegocio()
    {
        // D12: el vínculo acepta SOLO documentos de tipo Expediente.
        var ctx = Crear();
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(3)).ReturnsAsync(new DocumentoAdministrativo
        {
            Id = 3, Numero = "0001", Anio = 2026, Tipo = TipoDocumento.Oficio,
            Descripcion = "x", FechaEmision = DateTime.UtcNow, FechaRegistro = DateTime.UtcNow,
            Estado = EstadoDocumento.Pendiente,
        });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.CrearAsync(new Tarea { Titulo = "x", DocumentoAdministrativoId = 3 }));
    }

    [Fact]
    public async Task CrearAsync_DocumentoExpedienteCerrado_LanzaReglaDeNegocio()
    {
        var ctx = Crear();
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(3)).ReturnsAsync(new DocumentoAdministrativo
        {
            Id = 3, Numero = "0001", Anio = 2026, Tipo = TipoDocumento.Expediente,
            Descripcion = "x", FechaEmision = DateTime.UtcNow, FechaRegistro = DateTime.UtcNow,
            Estado = EstadoDocumento.Finalizado,
        });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.CrearAsync(new Tarea { Titulo = "x", DocumentoAdministrativoId = 3 }));
    }

    [Fact]
    public async Task CrearAsync_ClasificacionCompletaYValida_DelegaAlRepoConLosCincoIds()
    {
        var ctx = Crear();
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(1)).ReturnsAsync(new Zona { Id = 1, Nombre = "Centro", Activo = true });
        ctx.Dimensiones.Setup(d => d.ObtenerPorIdAsync(2)).ReturnsAsync(new DimensionTematica { Id = 2, Nombre = "Tránsito", Activo = true });
        ctx.Organismos.Setup(o => o.ObtenerPorIdAsync(3)).ReturnsAsync(new OrganismoResponsable { Id = 3, Nombre = "Intendencia", Activo = true });
        ctx.Origenes.Setup(o => o.ObtenerPorIdAsync(4)).ReturnsAsync(new OrigenFinanciamiento { Id = 4, Nombre = "Presupuesto propio", Activo = true });
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(5)).ReturnsAsync(new DocumentoAdministrativo
        {
            Id = 5, Numero = "0001", Anio = 2026, Tipo = TipoDocumento.Expediente,
            Descripcion = "x", FechaEmision = DateTime.UtcNow, FechaRegistro = DateTime.UtcNow,
            Estado = EstadoDocumento.EnProceso,
        });
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Tarea>())).ReturnsAsync(10);

        await ctx.Svc.CrearAsync(new Tarea
        {
            Titulo = "x", ZonaId = 1, DimensionTematicaId = 2, OrganismoResponsableId = 3,
            OrigenFinanciamientoId = 4, DocumentoAdministrativoId = 5,
        });

        ctx.Repo.Verify(r => r.AgregarAsync(It.Is<Tarea>(t =>
            t.ZonaId == 1 && t.DimensionTematicaId == 2 && t.OrganismoResponsableId == 3
            && t.OrigenFinanciamientoId == 4 && t.DocumentoAdministrativoId == 5)), Times.Once);
    }

    [Fact]
    public async Task CrearAsync_SinNingunClasificador_NoConsultaLosRepositoriosDeCatalogo()
    {
        // Caso mayoritario (D7: los cinco son opcionales) — no debería pagar el costo de 5
        // consultas de validación cuando el operador no clasificó nada.
        var ctx = Crear();
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Tarea>())).ReturnsAsync(1);

        await ctx.Svc.CrearAsync(new Tarea { Titulo = "x" });

        ctx.Zonas.Verify(z => z.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
        ctx.Dimensiones.Verify(d => d.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
        ctx.Organismos.Verify(o => o.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
        ctx.Origenes.Verify(o => o.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
        ctx.Documentos.Verify(d => d.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: FAIL con error de compilación — el constructor de `TareaService` no acepta 10 argumentos.

- [ ] **Step 3: Implementación mínima**

En `src/StockApp.Application/Tareas/TareaService.cs`, reemplazar el bloque de campos + constructor:

```csharp
public class TareaService : ITareaService
{
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

por:

```csharp
public class TareaService : ITareaService
{
    private readonly ITareaRepository      _repo;
    private readonly IUsuarioRepository    _usuarios;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger          _audit;

    // Clasificadores (spec 2026-09-08, D12): repositorios, NO servicios — validar
    // existencia/actividad acá no debe exigir catalogo.maestras (el permiso de ABM de
    // catálogos), que es completamente ajeno a tareas.gestionar/tareas.administrar.
    private readonly IZonaRepository                 _zonas;
    private readonly IDimensionTematicaRepository    _dimensiones;
    private readonly IOrganismoResponsableRepository _organismos;
    private readonly IOrigenFinanciamientoRepository _origenes;
    private readonly IDocumentoAdministrativoRepository _documentos;

    public TareaService(
        ITareaRepository repo, IUsuarioRepository usuarios, ICurrentSession session,
        IAuthorizationService auth, IAuditLogger audit,
        IZonaRepository zonas, IDimensionTematicaRepository dimensiones,
        IOrganismoResponsableRepository organismos, IOrigenFinanciamientoRepository origenes,
        IDocumentoAdministrativoRepository documentos)
    {
        _repo        = repo;
        _usuarios    = usuarios;
        _session     = session;
        _auth        = auth;
        _audit       = audit;
        _zonas       = zonas;
        _dimensiones = dimensiones;
        _organismos  = organismos;
        _origenes    = origenes;
        _documentos  = documentos;
    }
```

Reemplazar el método `CrearAsync`:

```csharp
    public async Task<int> CrearAsync(Tarea tarea)
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);

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

        var id = await _repo.AgregarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaTarea, "Tarea", id,
            $"Título: {tarea.Titulo}" +
            (tarea.FechaLimite is not null ? $"; Vence: {tarea.FechaLimite:yyyy-MM-dd}" : string.Empty));

        return id;
    }
```

por:

```csharp
    public async Task<int> CrearAsync(Tarea tarea)
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);

        if (string.IsNullOrWhiteSpace(tarea.Titulo))
            throw new ArgumentException("El título de la tarea es obligatorio.", nameof(tarea.Titulo));

        // D12 del spec: misma validación de clasificadores que ReclasificarAsync (Task 5) —
        // no hay atajo por la vía del alta.
        await ValidarClasificacionAsync(
            tarea.ZonaId, tarea.DimensionTematicaId, tarea.OrganismoResponsableId,
            tarea.OrigenFinanciamientoId, tarea.DocumentoAdministrativoId);

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

        var id = await _repo.AgregarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaTarea, "Tarea", id,
            $"Título: {tarea.Titulo}" +
            (tarea.FechaLimite is not null ? $"; Vence: {tarea.FechaLimite:yyyy-MM-dd}" : string.Empty));

        return id;
    }

    /// <summary>
    /// Validación compartida de los cinco clasificadores (spec 2026-09-08, D12): cada
    /// catálogo asignado debe existir y estar activo, y el documento debe ser de tipo
    /// Expediente y estar activo (EsActivo). La reutilizan CrearAsync y ReclasificarAsync
    /// (Task 5) SIN atajos — un Admin no puede colar un Oficio ni un catálogo inactivo por
    /// la vía de la reclasificación. Los cuatro ids nulos (caso mayoritario, D7) no disparan
    /// ninguna consulta.
    /// </summary>
    private async Task<(Zona? Zona, DimensionTematica? Dimension, OrganismoResponsable? Organismo,
                         OrigenFinanciamiento? Origen, DocumentoAdministrativo? Documento)>
        ValidarClasificacionAsync(
            int? zonaId, int? dimensionId, int? organismoId, int? origenId, int? documentoId)
    {
        Zona? zona = null;
        if (zonaId is int zid)
        {
            zona = await _zonas.ObtenerPorIdAsync(zid)
                ?? throw new ReglaDeNegocioException($"La zona {zid} no existe.");
            if (!zona.Activo)
                throw new ReglaDeNegocioException($"La zona '{zona.Nombre}' está inactiva.");
        }

        DimensionTematica? dimension = null;
        if (dimensionId is int did)
        {
            dimension = await _dimensiones.ObtenerPorIdAsync(did)
                ?? throw new ReglaDeNegocioException($"La dimensión temática {did} no existe.");
            if (!dimension.Activo)
                throw new ReglaDeNegocioException($"La dimensión temática '{dimension.Nombre}' está inactiva.");
        }

        OrganismoResponsable? organismo = null;
        if (organismoId is int oid)
        {
            organismo = await _organismos.ObtenerPorIdAsync(oid)
                ?? throw new ReglaDeNegocioException($"El organismo responsable {oid} no existe.");
            if (!organismo.Activo)
                throw new ReglaDeNegocioException($"El organismo responsable '{organismo.Nombre}' está inactivo.");
        }

        OrigenFinanciamiento? origen = null;
        if (origenId is int fid)
        {
            origen = await _origenes.ObtenerPorIdAsync(fid)
                ?? throw new ReglaDeNegocioException($"El origen de financiamiento {fid} no existe.");
            if (!origen.Activo)
                throw new ReglaDeNegocioException($"El origen de financiamiento '{origen.Nombre}' está inactivo.");
        }

        DocumentoAdministrativo? documento = null;
        if (documentoId is int docid)
        {
            documento = await _documentos.ObtenerPorIdAsync(docid)
                ?? throw new ReglaDeNegocioException($"El documento {docid} no existe.");
            if (documento.Tipo != TipoDocumento.Expediente)
                throw new ReglaDeNegocioException(
                    $"El documento {docid} no es un expediente (es {documento.Tipo}).");
            if (!documento.EsActivo)
                throw new ReglaDeNegocioException(
                    $"El expediente {documento.Numero}/{documento.Anio} no está activo.");
        }

        return (zona, dimension, organismo, origen, documento);
    }
```

`TareaService.cs` NO necesita ningún `using` nuevo para este Step: `DocumentoAdministrativo`/`TipoDocumento` ya llegan vía `using StockApp.Domain.Entities;`/`using StockApp.Domain.Enums;` (ya presentes en el archivo), e `IDocumentoAdministrativoRepository` vía `using StockApp.Application.Interfaces;` (también ya presente).

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: PASS (toda la clase — los tests preexistentes de Tomar/Soltar/Terminar/Cancelar/CambiarPrioridad/Notas siguen verdes porque el constructor sigue resolviendo sus mocks igual, solo con 5 params más).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Tareas/TareaService.cs tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs
git commit -m "feat(tareas): CrearAsync valida los cinco clasificadores contra sus catálogos"
```

---

### Task 5: `TareaService.ReclasificarAsync` — guard sin-cambios, alcanza terminales (D9), doble registro (D10)

**Files:**
- Create: `src/StockApp.Application/Tareas/DatosClasificacionTarea.cs`
- Modify: `src/StockApp.Application/Tareas/ITareaService.cs`
- Modify: `src/StockApp.Application/Tareas/TareaService.cs`
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs`
- Test: `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs`

**Interfaces:**
- Produces: `DatosClasificacionTarea` (record, reemplazo total D21), `ITareaService.ReclasificarAsync(int tareaId, DatosClasificacionTarea datos)`, `ITareaService.ObtenerPorIdAsync(int id)` (corrección de contrato: expone `ITareaRepository.ObtenerPorIdAsync`, ya existente, con sus mismos Includes — evita que `TareaFormViewModel.ReclasificarAsync`, Task 12, tenga que traer TODAS las tareas con `ListarAsync()` solo para quedarse con una), `AccionAuditada.ReclasificacionTarea` — consumidos por Task 6 (Api) y Task 12 (Presentation).

- [ ] **Step 1: Escribir el test que falla**

Crear `src/StockApp.Application/Tareas/DatosClasificacionTarea.cs`:

```csharp
namespace StockApp.Application.Tareas;

/// <summary>
/// Datos de clasificación de una tarea (spec 2026-09-08, D21): REEMPLAZO TOTAL de los cinco
/// campos en cada llamada a ITareaService.ReclasificarAsync — null en un campo significa
/// desasignar explícitamente ese clasificador, no "no tocar". Esta semántica es la que
/// permite que el modal de reclasificación (Presentation, Task 11) precargado funcione sin
/// ambigüedad: lo que el Admin ve en el modal es exactamente lo que queda guardado si no
/// toca nada.
/// </summary>
public record DatosClasificacionTarea(
    int? ZonaId,
    int? DimensionTematicaId,
    int? OrganismoResponsableId,
    int? OrigenFinanciamientoId,
    int? DocumentoAdministrativoId);
```

Agregar a `src/StockApp.Application/Tareas/ITareaService.cs`, dentro de la interfaz, después de `AgregarNotaAsync`:

```csharp
    /// <summary>Nota manual. Las notas son append-only: no hay método para editarlas ni
    /// borrarlas. Implementado en Task 6.</summary>
    Task AgregarNotaAsync(int id, string texto);

    /// <summary>Una sola tarea por id, o null si no existe (spec 2026-09-08). Expone
    /// ITareaRepository.ObtenerPorIdAsync, que ya existía con sus Includes completos —
    /// TareaFormViewModel.ReclasificarAsync (Task 12) lo usa para refrescar la tarea
    /// reclasificada sin traer la lista completa.</summary>
    Task<Tarea?> ObtenerPorIdAsync(int id);

    /// <summary>Reemplaza los cinco clasificadores (spec 2026-09-08, D21: total, no parcial —
    /// null desasigna). Solo Admin (AdministrarTareas). Alcanza también tareas terminales
    /// (D9): reclasificar NO cambia el estado ni reabre la tarea. Misma validación que
    /// CrearAsync (D12). Si nada cambia, no genera nota ni auditoría (mismo guard que
    /// CambiarPrioridadAsync).</summary>
    Task ReclasificarAsync(int tareaId, DatosClasificacionTarea datos);

    /// <summary>Tareas vinculadas a un expediente (D11/D13): la dependencia va en un solo
    /// sentido, Documentos pregunta acá.</summary>
    Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId);
```

Agregar a `src/StockApp.Domain/Enums/AccionAuditada.cs`, al final del enum (**ver Global Constraints: confirmar el último valor REAL del archivo antes de escribir el número — acá se muestra 72, el siguiente libre después del bloque 60–71 que agrega el plan de catálogos, que corre antes que este**):

```csharp

    // ── Tareas — Clasificadores (append-only a partir de 72, después del bloque
    // 60–71 del plan de catálogos) ────────────────────────────────────────────
    ReclasificacionTarea = 72,
}
```

(reemplaza el `}` de cierre del enum, que hoy está inmediatamente después de `ModificacionOrigenFinanciamiento = 71,`, agregado por el plan de catálogos — o después del último bloque REALMENTE presente en el archivo al momento de ejecutar esta task, si algo cambió).

Agregar al final de la clase `TareaServiceTests` (antes del último `}`):

```csharp

    // ── ObtenerPorIdAsync (spec 2026-09-08 — corrección de contrato) ────────────

    [Fact]
    public async Task ObtenerPorIdAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.ObtenerPorIdAsync(1));
    }

    [Fact]
    public async Task ObtenerPorIdAsync_DelegaAlRepo()
    {
        var ctx = Crear();
        var tarea = new Tarea { Id = 5, Titulo = "x" };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        var resultado = await ctx.Svc.ObtenerPorIdAsync(5);

        Assert.Same(tarea, resultado);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_TareaInexistente_DevuelveNull()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(999)).ReturnsAsync((Tarea?)null);

        var resultado = await ctx.Svc.ObtenerPorIdAsync(999);

        Assert.Null(resultado);
    }

    // ── ReclasificarAsync (spec 2026-09-08) ─────────────────────────────────────

    [Fact]
    public async Task ReclasificarAsync_ComoOperador_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        ctx.Auth.Setup(a => a.Verificar(It.Is<ICurrentSession>(s => s.RolActual == RolUsuario.Operador), Permisos.AdministrarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(null, null, null, null, null)));

        ctx.Repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ReclasificarAsync_TareaInexistente_LanzaEntidadNoEncontrada()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync((Tarea?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(null, null, null, null, null)));
    }

    [Fact]
    public async Task ReclasificarAsync_ZonaInactiva_LanzaReglaDeNegocioSinTocarElRepo()
    {
        // D12: misma validación que CrearAsync — un Admin no puede colar un catálogo
        // inactivo por la vía de la reclasificación.
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(9)).ReturnsAsync(new Zona { Id = 9, Nombre = "Centro", Activo = false });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(9, null, null, null, null)));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Tarea>()), Times.Never);
    }

    [Fact]
    public async Task ReclasificarAsync_DocumentoNoEsExpediente_LanzaReglaDeNegocio()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(4)).ReturnsAsync(new DocumentoAdministrativo
        {
            Id = 4, Numero = "0002", Anio = 2026, Tipo = TipoDocumento.Suministro,
            Descripcion = "x", FechaEmision = DateTime.UtcNow, FechaRegistro = DateTime.UtcNow,
            Estado = EstadoDocumento.Pendiente,
        });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(null, null, null, null, 4)));
    }

    [Fact]
    public async Task ReclasificarAsync_SinCambios_NoGeneraNotaNiAuditoriaNiPersiste()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente,
            ZonaId = 3, DimensionTematicaId = null, OrganismoResponsableId = null,
            OrigenFinanciamientoId = null, DocumentoAdministrativoId = null,
        };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(3)).ReturnsAsync(new Zona { Id = 3, Nombre = "Centro", Activo = true });

        await ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(3, null, null, null, null));

        Assert.Empty(tarea.Notas);
        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Tarea>()), Times.Never);
        ctx.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ReclasificacionTarea, It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ReclasificarAsync_TareaTerminada_AplicaLaClasificacionYNoCambiaElEstado()
    {
        // D9 del spec: la reclasificación alcanza también a tareas terminales, y NO las reabre.
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Terminada };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(3)).ReturnsAsync(new Zona { Id = 3, Nombre = "Centro", Activo = true });

        await ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(3, null, null, null, null));

        Assert.Equal(EstadoTarea.Terminada, tarea.Estado);
        Assert.Equal(3, tarea.ZonaId);
    }

    [Fact]
    public async Task ReclasificarAsync_CambiaZonaYDimension_GeneraNotaAutomaticaConElDiffLegible()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente,
            ZonaId = null, DimensionTematicaId = 8,
            DimensionTematica = new DimensionTematica { Id = 8, Nombre = "Tránsito", Activo = true },
        };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(3)).ReturnsAsync(new Zona { Id = 3, Nombre = "Centro", Activo = true });
        ctx.Dimensiones.Setup(d => d.ObtenerPorIdAsync(6)).ReturnsAsync(new DimensionTematica { Id = 6, Nombre = "Infraestructura", Activo = true });

        await ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(3, 6, null, null, null));

        var nota = Assert.Single(tarea.Notas);
        Assert.True(nota.EsAutomatica);
        Assert.Equal(
            "admin reclasificó — Zona: (sin asignar) → Centro; Dimensión: Tránsito → Infraestructura",
            nota.Texto);
        ctx.Repo.Verify(r => r.ActualizarAsync(tarea), Times.Once);
    }

    [Fact]
    public async Task ReclasificarAsync_DesasignaUnClasificadorExistente_QuedaEnNullYSeRegistra()
    {
        // D21: null es una desasignación explícita, no "no tocar".
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente,
            ZonaId = 3, Zona = new Zona { Id = 3, Nombre = "Centro", Activo = true },
        };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.ReclasificarAsync(1, new DatosClasificacionTarea(null, null, null, null, null));

        Assert.Null(tarea.ZonaId);
        var nota = Assert.Single(tarea.Notas);
        Assert.Equal("admin reclasificó — Zona: Centro → (sin asignar)", nota.Texto);
    }

    [Fact]
    public async Task ReclasificarAsync_RegistraAuditoriaConElMismoTextoDeLaNota()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);
        ctx.Zonas.Setup(z => z.ObtenerPorIdAsync(1)).ReturnsAsync(new Zona { Id = 1, Nombre = "Centro", Activo = true });

        await ctx.Svc.ReclasificarAsync(5, new DatosClasificacionTarea(1, null, null, null, null));

        ctx.Audit.Verify(a => a.RegistrarAsync(
            1, AccionAuditada.ReclasificacionTarea, "Tarea", 5,
            "Zona: (sin asignar) → Centro"),
            Times.Once);
    }

    // ── ListarPorDocumentoAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ListarPorDocumentoAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.ListarPorDocumentoAsync(1));
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_DelegaAlRepo()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ListarPorDocumentoAsync(7))
            .ReturnsAsync(new List<Tarea> { new() { Id = 1, Titulo = "x", DocumentoAdministrativoId = 7 } });

        var tareas = await ctx.Svc.ListarPorDocumentoAsync(7);

        Assert.Single(tareas);
    }

    [Fact]
    public void ITareaService_ExponeExactamenteLosMetodosEsperados()
    {
        var esperados = new HashSet<string>
        {
            nameof(ITareaService.CrearAsync),
            nameof(ITareaService.ListarAsync),
            nameof(ITareaService.TomarAsync),
            nameof(ITareaService.SoltarAsync),
            nameof(ITareaService.TerminarAsync),
            nameof(ITareaService.CancelarAsync),
            nameof(ITareaService.CambiarPrioridadAsync),
            nameof(ITareaService.AgregarNotaAsync),
            nameof(ITareaService.ObtenerPorIdAsync),
            nameof(ITareaService.ReclasificarAsync),
            nameof(ITareaService.ListarPorDocumentoAsync),
        };

        var metodos = typeof(ITareaService).GetMethods().Select(m => m.Name).ToHashSet();

        Assert.Equal(esperados, metodos);
    }
```

Este último test REEMPLAZA al `ITareaService_ExponeExactamenteLosMetodosEsperados` ya existente en el archivo (mismo nombre, whitelist ampliada con los 3 métodos nuevos) — borrar la versión vieja (la que tiene 8 elementos en `esperados`) antes de agregar esta.

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: FAIL con error de compilación — `ITareaService` no tiene `ReclasificarAsync` ni `ListarPorDocumentoAsync`, `DatosClasificacionTarea` no existe, `AccionAuditada.ReclasificacionTarea` no existe.

- [ ] **Step 3: Implementación mínima**

En `src/StockApp.Application/Tareas/TareaService.cs`, agregar después de `AgregarNotaAsync` (al final de la clase, antes del último `}`):

```csharp

    public async Task<Tarea?> ObtenerPorIdAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);
        return await _repo.ObtenerPorIdAsync(id);
    }

    public async Task ReclasificarAsync(int tareaId, DatosClasificacionTarea datos)
    {
        _auth.Verificar(_session, Permisos.AdministrarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(tareaId)
            ?? throw new EntidadNoEncontradaException($"Tarea {tareaId} no encontrada.");

        // D12: misma validación que CrearAsync, sin atajos.
        var (zona, dimension, organismo, origen, documento) = await ValidarClasificacionAsync(
            datos.ZonaId, datos.DimensionTematicaId, datos.OrganismoResponsableId,
            datos.OrigenFinanciamientoId, datos.DocumentoAdministrativoId);

        var huboCambios =
            tarea.ZonaId != datos.ZonaId ||
            tarea.DimensionTematicaId != datos.DimensionTematicaId ||
            tarea.OrganismoResponsableId != datos.OrganismoResponsableId ||
            tarea.OrigenFinanciamientoId != datos.OrigenFinanciamientoId ||
            tarea.DocumentoAdministrativoId != datos.DocumentoAdministrativoId;

        // Guard "sin cambios" (D10 del spec, mismo patrón que CambiarPrioridadAsync,
        // TareaService.cs): si la reclasificación no cambia ningún valor, no se genera
        // nota automática ni entrada de auditoría.
        if (!huboCambios)
            return;

        var diff = ConstruirDiffClasificacion(tarea, zona, dimension, organismo, origen, documento);

        // D9: reclasificar NO toca Estado — alcanza también tareas Terminada/Cancelada.
        tarea.ZonaId = datos.ZonaId;
        tarea.DimensionTematicaId = datos.DimensionTematicaId;
        tarea.OrganismoResponsableId = datos.OrganismoResponsableId;
        tarea.OrigenFinanciamientoId = datos.OrigenFinanciamientoId;
        tarea.DocumentoAdministrativoId = datos.DocumentoAdministrativoId;

        // D10: doble registro — nota automática en el hilo + entrada de auditoría, mismo
        // patrón que AnularAsync en Documentos.
        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId    = _session.UsuarioActual!.Id,
            Fecha        = DateTime.UtcNow,
            Texto        = $"admin reclasificó — {diff}",
            EsAutomatica = true,
        });

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.ReclasificacionTarea, "Tarea", tareaId, diff);
    }

    /// <summary>
    /// Diff legible de la reclasificación (D10 del spec), ej.: "Zona: (sin asignar) →
    /// Centro; Dimensión: Tránsito → Infraestructura". Solo incluye los campos que
    /// efectivamente cambiaron — <see cref="ReclasificarAsync"/> ya filtró el caso "sin
    /// cambios" antes de llamar acá, pero un cambio parcial (ej. solo Zona) no debe listar
    /// los otros cuatro campos sin cambiar.
    /// </summary>
    private static string ConstruirDiffClasificacion(
        Tarea tarea, Zona? nuevaZona, DimensionTematica? nuevaDimension,
        OrganismoResponsable? nuevoOrganismo, OrigenFinanciamiento? nuevoOrigen,
        DocumentoAdministrativo? nuevoDocumento)
    {
        var partes = new List<string>();

        void AgregarSiCambio(string etiqueta, string? anterior, string? nuevo)
        {
            var anteriorTexto = anterior ?? "(sin asignar)";
            var nuevoTexto = nuevo ?? "(sin asignar)";
            if (anteriorTexto != nuevoTexto)
                partes.Add($"{etiqueta}: {anteriorTexto} → {nuevoTexto}");
        }

        AgregarSiCambio("Zona", tarea.Zona?.Nombre, nuevaZona?.Nombre);
        AgregarSiCambio("Dimensión", tarea.DimensionTematica?.Nombre, nuevaDimension?.Nombre);
        AgregarSiCambio("Organismo", tarea.OrganismoResponsable?.Nombre, nuevoOrganismo?.Nombre);
        AgregarSiCambio("Origen de financiamiento", tarea.OrigenFinanciamiento?.Nombre, nuevoOrigen?.Nombre);
        AgregarSiCambio(
            "Expediente",
            tarea.DocumentoAdministrativo is null ? null : $"{tarea.DocumentoAdministrativo.Numero}/{tarea.DocumentoAdministrativo.Anio}",
            nuevoDocumento is null ? null : $"{nuevoDocumento.Numero}/{nuevoDocumento.Anio}");

        return string.Join("; ", partes);
    }

    public async Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId)
    {
        _auth.Verificar(_session, Permisos.GestionarTareas);
        return await _repo.ListarPorDocumentoAsync(documentoId);
    }
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: PASS (toda la clase).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Tareas/DatosClasificacionTarea.cs src/StockApp.Application/Tareas/ITareaService.cs src/StockApp.Application/Tareas/TareaService.cs src/StockApp.Domain/Enums/AccionAuditada.cs tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs
git commit -m "feat(tareas): ObtenerPorIdAsync + ReclasificarAsync — alcanza terminales, guard sin-cambios, doble registro"
```

---

### Task 6: `TareasEndpoints` — request/DTO extendidos, `PUT .../clasificacion`, `GET ?documentoId=`

**Files:**
- Modify: `src/StockApp.Api/Endpoints/TareasEndpoints.cs`
- Test: `tests/StockApp.Api.Tests/TareasEndpointTests.cs`

**Interfaces:**
- Consumes: `ITareaService.ObtenerPorIdAsync`/`ReclasificarAsync`/`ListarPorDocumentoAsync` (Task 5), `DatosClasificacionTarea`.
- Produces: `GET /tareas/{id}` (policy `GestionarTareas`, 404 si no existe — hoy `TareasEndpoints` NO tiene GET por id, solo la lista; verificado leyendo el archivo real antes de este task), `PUT /tareas/{id}/clasificacion` (policy `AdministrarTareas`), `GET /tareas?documentoId={id}` (mismo endpoint que `GET /tareas`, policy `GestionarTareas`), `CrearTareaRequest`/`TareaDto` extendidos.

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Api.Tests/TareasEndpointTests.cs` (al final de la clase, antes del último `}`):

```csharp

    [Fact]
    public async Task GetTarea_Existente_Devuelve200ConSuDto()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var id = await CrearTareaAsync(client, "Reparar bache");

        var response = await client.GetAsync($"/tareas/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tarea = await response.Content.ReadFromJsonAsync<TareaDto>();
        Assert.Equal("Reparar bache", tarea!.Titulo);
    }

    [Fact]
    public async Task GetTarea_Inexistente_Devuelve404()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/tareas/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostTareas_ConZonaInexistente_Devuelve409()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var json = """{"titulo":"x","zonaId":9999}""";
        var response = await client.PostAsync("/tareas",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutClasificacion_ConTokenOperador_Devuelve403()
    {
        await SeedUsuariosAsync();
        var clienteAdmin = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(clienteAdmin);
        var clienteOperador = ClienteAutenticado(TokenOperador());

        var response = await clienteOperador.PutAsJsonAsync(
            $"/tareas/{id}/clasificacion",
            new ClasificarTareaRequest(null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutClasificacion_ConTokenAdminSinCambios_Devuelve200()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(client);

        var response = await client.PutAsJsonAsync(
            $"/tareas/{id}/clasificacion",
            new ClasificarTareaRequest(null, null, null, null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PutClasificacion_TareaTerminada_Devuelve200YAplicaLaClasificacion()
    {
        // D9 del spec: la reclasificación alcanza también a tareas terminales — a
        // diferencia de PostPrioridad_TareaTerminada_Devuelve409 (Task existente), acá NO
        // debe dar 409.
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(client);
        await client.PostAsync($"/tareas/{id}/tomar", content: null);
        await client.PostAsync($"/tareas/{id}/terminar", content: null);

        var response = await client.PutAsJsonAsync(
            $"/tareas/{id}/clasificacion",
            new ClasificarTareaRequest(null, null, null, null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var ctx = Factory.CrearContexto();
        var tarea = await ctx.Tareas.SingleAsync(t => t.Id == id);
        Assert.Equal(EstadoTarea.Terminada, tarea.Estado);
    }

    [Fact]
    public async Task GetTareas_ConDocumentoId_DevuelveSoloLasVinculadas()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        await CrearTareaAsync(client, "Sin expediente");

        var response = await client.GetAsync("/tareas?documentoId=9999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tareas = await response.Content.ReadFromJsonAsync<List<TareaDto>>();
        Assert.Empty(tareas!);
    }
```

Agregar el `using System.Net.Http.Json;` si no está ya en el archivo (ya está, usado por `PostAsJsonAsync`); agregar `using StockApp.Application.Tareas;` si `DatosClasificacionTarea`/`ClasificarTareaRequest` lo requieren (`ClasificarTareaRequest` vive en `StockApp.Api.Endpoints`, ya importado vía `using StockApp.Api.Endpoints;`).

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~TareasEndpointTests"`
Expected: FAIL con error de compilación — `CrearTareaRequest` no tiene un constructor con `zonaId` nombrado, `ClasificarTareaRequest` no existe. (Los dos tests de `GetTarea_*` no rompen la compilación por sí solos, ya que `/tareas/{id}` responde 404 hoy por ausencia de ruta — pero corren en rojo junto con el resto por el error de compilación general del archivo.)

- [ ] **Step 3: Implementación mínima**

En `src/StockApp.Api/Endpoints/TareasEndpoints.cs`, reemplazar el bloque de records y el `TareaDto`:

```csharp
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
```

por:

```csharp
public record NotaTareaDto(int Id, int UsuarioId, DateTime Fecha, string Texto, bool EsAutomatica);

public record TareaDto(
    int Id, string Titulo, string? Descripcion,
    EstadoTarea Estado, PrioridadTarea Prioridad, DateTime? FechaLimite,
    int CreadaPorUsuarioId, DateTime FechaCreacion,
    int? TomadaPorUsuarioId, string? TomadaPorNombre, DateTime? FechaInicio,
    int? CerradaPorUsuarioId, DateTime? FechaFin,
    int? ZonaId, string? ZonaNombre,
    int? DimensionTematicaId, string? DimensionTematicaNombre,
    int? OrganismoResponsableId, string? OrganismoResponsableNombre,
    int? OrigenFinanciamientoId, string? OrigenFinanciamientoNombre,
    int? DocumentoAdministrativoId, string? DocumentoAdministrativoNumero,
    List<NotaTareaDto> Notas);

// Los 5 ids de clasificación con default null (bugfix de compatibilidad): los sitios de
// test/producción existentes que construyen CrearTareaRequest con 3 argumentos posicionales
// (Titulo, Descripcion, FechaLimite) siguen compilando sin tocarlos.
public record CrearTareaRequest(
    string Titulo, string? Descripcion, DateTime? FechaLimite,
    int? ZonaId = null, int? DimensionTematicaId = null, int? OrganismoResponsableId = null,
    int? OrigenFinanciamientoId = null, int? DocumentoAdministrativoId = null);

public record CambiarPrioridadRequest(PrioridadTarea Prioridad);
public record AgregarNotaRequest(string Texto);
public record TareaCreadaResponse(int Id);

/// <summary>Espejo HTTP de DatosClasificacionTarea (Application) — reemplazo total, D21:
/// null en un campo desasigna ese clasificador.</summary>
public record ClasificarTareaRequest(
    int? ZonaId, int? DimensionTematicaId, int? OrganismoResponsableId,
    int? OrigenFinanciamientoId, int? DocumentoAdministrativoId);
```

Reemplazar el cuerpo de `MapTareasEndpoints`:

```csharp
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
```

por:

```csharp
        group.MapPost("/", async (CrearTareaRequest request, ITareaService service) =>
        {
            var tarea = new Tarea
            {
                Titulo      = request.Titulo,
                Descripcion = request.Descripcion,
                FechaLimite = request.FechaLimite,
                ZonaId                  = request.ZonaId,
                DimensionTematicaId     = request.DimensionTematicaId,
                OrganismoResponsableId  = request.OrganismoResponsableId,
                OrigenFinanciamientoId  = request.OrigenFinanciamientoId,
                DocumentoAdministrativoId = request.DocumentoAdministrativoId,
            };
            var id = await service.CrearAsync(tarea);
            return Results.Created((string?)null, new TareaCreadaResponse(id));
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        // GET por id (corrección de contrato, spec 2026-09-08): no existía — mismo molde
        // exacto que DocumentosEndpoints.MapGet("/{id:int}", ...) (documento is null ?
        // Results.NotFound() : Results.Ok(ADto(documento))).
        group.MapGet("/{id:int}", async (int id, ITareaService service) =>
        {
            var tarea = await service.ObtenerPorIdAsync(id);
            return tarea is null ? Results.NotFound() : Results.Ok(ADto(tarea));
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        // documentoId es opcional (Minimal API lo deja null si no viene en la query string):
        // sin él, el comportamiento es idéntico al GET /tareas de siempre; con él, delega en
        // ListarPorDocumentoAsync (D11/D13 del spec: el endpoint vive en Tareas, no en
        // Documentos). Mismo permiso (GestionarTareas) en los dos casos.
        group.MapGet("/", async (int? documentoId, ITareaService service) =>
        {
            var tareas = documentoId is int did
                ? await service.ListarPorDocumentoAsync(did)
                : await service.ListarAsync();
            return Results.Ok(tareas.Select(ADto));
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        group.MapPut("/{id:int}/clasificacion", async (int id, ClasificarTareaRequest request, ITareaService service) =>
        {
            await service.ReclasificarAsync(id, new DatosClasificacionTarea(
                request.ZonaId, request.DimensionTematicaId, request.OrganismoResponsableId,
                request.OrigenFinanciamientoId, request.DocumentoAdministrativoId));
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarTareas);
```

Reemplazar el método `ADto` final:

```csharp
    private static TareaDto ADto(Tarea t) => new(
        t.Id, t.Titulo, t.Descripcion,
        t.Estado, t.Prioridad, t.FechaLimite,
        t.CreadaPorUsuarioId, t.FechaCreacion,
        t.TomadaPorUsuarioId, t.TomadaPor?.NombreUsuario, t.FechaInicio,
        t.CerradaPorUsuarioId, t.FechaFin,
        t.Notas.OrderBy(n => n.Fecha).ThenBy(n => n.Id)
            .Select(n => new NotaTareaDto(n.Id, n.UsuarioId, n.Fecha, n.Texto, n.EsAutomatica))
            .ToList());
```

por:

```csharp
    private static TareaDto ADto(Tarea t) => new(
        t.Id, t.Titulo, t.Descripcion,
        t.Estado, t.Prioridad, t.FechaLimite,
        t.CreadaPorUsuarioId, t.FechaCreacion,
        t.TomadaPorUsuarioId, t.TomadaPor?.NombreUsuario, t.FechaInicio,
        t.CerradaPorUsuarioId, t.FechaFin,
        t.ZonaId, t.Zona?.Nombre,
        t.DimensionTematicaId, t.DimensionTematica?.Nombre,
        t.OrganismoResponsableId, t.OrganismoResponsable?.Nombre,
        t.OrigenFinanciamientoId, t.OrigenFinanciamiento?.Nombre,
        t.DocumentoAdministrativoId, t.DocumentoAdministrativo?.Numero,
        t.Notas.OrderBy(n => n.Fecha).ThenBy(n => n.Id)
            .Select(n => new NotaTareaDto(n.Id, n.UsuarioId, n.Fecha, n.Texto, n.EsAutomatica))
            .ToList());
```

Agregar `using StockApp.Application.Tareas;` al principio del archivo (por `DatosClasificacionTarea`) si no está ya importado.

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~TareasEndpointTests"`
Expected: PASS (toda la clase, incluidos los tests preexistentes que llaman `new CrearTareaRequest(titulo, null, null)`).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Api/Endpoints/TareasEndpoints.cs tests/StockApp.Api.Tests/TareasEndpointTests.cs
git commit -m "feat(tareas): agrega GET /tareas/{id} y expone clasificación y vínculo con documentos"
```

---

### Task 7: `TareaApiClient` — extensión completa de `ITareaService`

**Files:**
- Modify: `src/StockApp.ApiClient/TareaApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/TareaApiClientTests.cs`

**Interfaces:**
- Consumes: `GET /tareas/{id}`, `PUT /tareas/{id}/clasificacion`, `GET /tareas?documentoId=`, `TareaDto`/`CrearTareaRequest`/`ClasificarTareaRequest` (Task 6).
- Produces: `TareaApiClient` implementa `ITareaService` completo (Task 5), incluido `ObtenerPorIdAsync` (corrección de contrato).

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.ApiClient.Tests/TareaApiClientTests.cs` (al final de la clase, antes del último `}`):

```csharp

    [Fact]
    public async Task ObtenerPorIdAsync_200_DeserializaLaTarea()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            id = 5, titulo = "Reparar bache", descripcion = (string?)null,
            estado = EstadoTarea.Pendiente, prioridad = PrioridadTarea.Media, fechaLimite = (DateTime?)null,
            creadaPorUsuarioId = 1, fechaCreacion = new DateTime(2026, 9, 1),
            tomadaPorUsuarioId = (int?)null, tomadaPorNombre = (string?)null, fechaInicio = (DateTime?)null,
            cerradaPorUsuarioId = (int?)null, fechaFin = (DateTime?)null,
            zonaId = (int?)null, zonaNombre = (string?)null,
            dimensionTematicaId = (int?)null, dimensionTematicaNombre = (string?)null,
            organismoResponsableId = (int?)null, organismoResponsableNombre = (string?)null,
            origenFinanciamientoId = (int?)null, origenFinanciamientoNombre = (string?)null,
            documentoAdministrativoId = (int?)null, documentoAdministrativoNumero = (string?)null,
            notas = Array.Empty<object>(),
        }));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var tarea = await client.ObtenerPorIdAsync(5);

        Assert.Equal("/tareas/5", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Equal("Reparar bache", tarea!.Titulo);
    }

    [Fact]
    public async Task ObtenerPorIdAsync_404_DevuelveNull()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(HttpStatusCode.NotFound, "Tarea 999 no encontrada."));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var tarea = await client.ObtenerPorIdAsync(999);

        Assert.Null(tarea);
    }

    [Fact]
    public async Task CrearAsync_ConClasificacion_SerializaLosCincoIds()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 1 }, HttpStatusCode.Created));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await client.CrearAsync(new Tarea
        {
            Titulo = "x", ZonaId = 3, DimensionTematicaId = 4,
            OrganismoResponsableId = 5, OrigenFinanciamientoId = 6, DocumentoAdministrativoId = 7,
        });

        Assert.Contains("\"zonaId\":3", fake.UltimoBody);
        Assert.Contains("\"dimensionTematicaId\":4", fake.UltimoBody);
        Assert.Contains("\"organismoResponsableId\":5", fake.UltimoBody);
        Assert.Contains("\"origenFinanciamientoId\":6", fake.UltimoBody);
        Assert.Contains("\"documentoAdministrativoId\":7", fake.UltimoBody);
    }

    [Fact]
    public async Task ListarAsync_ConClasificacion_DeserializaLosNombres()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, titulo = "x", descripcion = (string?)null,
                estado = EstadoTarea.Pendiente, prioridad = PrioridadTarea.Media, fechaLimite = (DateTime?)null,
                creadaPorUsuarioId = 1, fechaCreacion = new DateTime(2026, 9, 1),
                tomadaPorUsuarioId = (int?)null, tomadaPorNombre = (string?)null, fechaInicio = (DateTime?)null,
                cerradaPorUsuarioId = (int?)null, fechaFin = (DateTime?)null,
                zonaId = 3, zonaNombre = "Centro",
                dimensionTematicaId = (int?)null, dimensionTematicaNombre = (string?)null,
                organismoResponsableId = (int?)null, organismoResponsableNombre = (string?)null,
                origenFinanciamientoId = (int?)null, origenFinanciamientoNombre = (string?)null,
                documentoAdministrativoId = (int?)null, documentoAdministrativoNumero = (string?)null,
                notas = Array.Empty<object>(),
            },
        }));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var tareas = await client.ListarAsync();

        var tarea = Assert.Single(tareas);
        Assert.Equal(3, tarea.ZonaId);
        Assert.Equal("Centro", tarea.Zona!.Nombre);
    }

    [Fact]
    public async Task ReclasificarAsync_PUTClasificacion_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Vacio(HttpStatusCode.OK));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await client.ReclasificarAsync(5, new DatosClasificacionTarea(1, null, null, null, null));

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/tareas/5/clasificacion", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"zonaId\":1", fake.UltimoBody);
        Assert.Contains("\"dimensionTematicaId\":null", fake.UltimoBody);
    }

    [Fact]
    public async Task ReclasificarAsync_403_LanzaUnauthorizedAccessException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(HttpStatusCode.Forbidden, "sin permiso"));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.ReclasificarAsync(5, new DatosClasificacionTarea(null, null, null, null, null)));
    }

    [Fact]
    public async Task ListarPorDocumentoAsync_GETConQueryString_DeserializaLaLista()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var tareas = await client.ListarPorDocumentoAsync(9);

        Assert.Equal("/tareas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("documentoId=9", fake.UltimaRequest.RequestUri!.Query);
        Assert.Empty(tareas);
    }
```

Confirmar (leyendo el archivo) que `TestHttp.Vacio(HttpStatusCode)` existe entre los helpers de `StockApp.ApiClient.Tests.TestInfra` — si no existe con ese nombre exacto, usar el helper equivalente ya presente en el archivo para una respuesta 200 sin body (mismo mecanismo que usan los tests de `TomarAsync`/`SoltarAsync`/`CancelarAsync` en este mismo archivo).

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~TareaApiClientTests"`
Expected: FAIL con error de compilación — `TareaApiClient` no tiene `ObtenerPorIdAsync` ni `ReclasificarAsync` ni `ListarPorDocumentoAsync`, `TareaWire`/`CrearTareaBody` no tienen los campos de clasificación.

- [ ] **Step 3: Implementación mínima**

En `src/StockApp.ApiClient/TareaApiClient.cs`, reemplazar los records internos:

```csharp
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
```

por:

```csharp
internal sealed record NotaTareaWire(int Id, int UsuarioId, DateTime Fecha, string Texto, bool EsAutomatica);

internal sealed record TareaWire(
    int Id, string Titulo, string? Descripcion,
    EstadoTarea Estado, PrioridadTarea Prioridad, DateTime? FechaLimite,
    int CreadaPorUsuarioId, DateTime FechaCreacion,
    int? TomadaPorUsuarioId, string? TomadaPorNombre, DateTime? FechaInicio,
    int? CerradaPorUsuarioId, DateTime? FechaFin,
    int? ZonaId, string? ZonaNombre,
    int? DimensionTematicaId, string? DimensionTematicaNombre,
    int? OrganismoResponsableId, string? OrganismoResponsableNombre,
    int? OrigenFinanciamientoId, string? OrigenFinanciamientoNombre,
    int? DocumentoAdministrativoId, string? DocumentoAdministrativoNumero,
    List<NotaTareaWire> Notas);

internal sealed record CrearTareaBody(
    string Titulo, string? Descripcion, DateTime? FechaLimite,
    int? ZonaId, int? DimensionTematicaId, int? OrganismoResponsableId,
    int? OrigenFinanciamientoId, int? DocumentoAdministrativoId);

internal sealed record CambiarPrioridadBody(PrioridadTarea Prioridad);
internal sealed record AgregarNotaBody(string Texto);
internal sealed record ClasificarTareaBody(
    int? ZonaId, int? DimensionTematicaId, int? OrganismoResponsableId,
    int? OrigenFinanciamientoId, int? DocumentoAdministrativoId);
```

Agregar `using StockApp.Domain.Exceptions;` al principio del archivo (por `EntidadNoEncontradaException`, todavía no importado en este archivo).

Agregar el método nuevo antes de `CrearAsync` (mismo molde EXACTO que `DocumentoApiClient.ObtenerPorIdAsync`):

```csharp
    public async Task<Tarea?> ObtenerPorIdAsync(int id)
    {
        try
        {
            var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"tareas/{id}"));
            await ApiErrores.AsegurarExitoAsync(response);

            var dto = await response.Content.ReadFromJsonAsync<TareaWire>();
            return dto is null ? null : AEntidad(dto);
        }
        catch (EntidadNoEncontradaException)
        {
            return null;  // 404 = tarea inexistente: contrato de la interfaz (null)
        }
    }
```

Reemplazar `CrearAsync`:

```csharp
    public async Task<int> CrearAsync(Tarea tarea)
    {
        var body = new CrearTareaBody(tarea.Titulo, tarea.Descripcion, tarea.FechaLimite);
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsJsonAsync("tareas", body));
        await ApiErrores.AsegurarExitoAsync(response);

        var creada = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la tarea.");
        return creada.Id;
    }
```

por:

```csharp
    public async Task<int> CrearAsync(Tarea tarea)
    {
        var body = new CrearTareaBody(
            tarea.Titulo, tarea.Descripcion, tarea.FechaLimite,
            tarea.ZonaId, tarea.DimensionTematicaId, tarea.OrganismoResponsableId,
            tarea.OrigenFinanciamientoId, tarea.DocumentoAdministrativoId);
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsJsonAsync("tareas", body));
        await ApiErrores.AsegurarExitoAsync(response);

        var creada = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la tarea.");
        return creada.Id;
    }
```

Agregar después de `CambiarPrioridadAsync`:

```csharp
    public async Task ReclasificarAsync(int id, DatosClasificacionTarea datos)
    {
        var body = new ClasificarTareaBody(
            datos.ZonaId, datos.DimensionTematicaId, datos.OrganismoResponsableId,
            datos.OrigenFinanciamientoId, datos.DocumentoAdministrativoId);
        var response = await ApiErrores.EnviarAsync(() => _http.PutAsJsonAsync($"tareas/{id}/clasificacion", body));
        await ApiErrores.AsegurarExitoAsync(response);
    }
```

Agregar después de `ListarAsync`:

```csharp
    public async Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"tareas?documentoId={documentoId}"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<TareaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }
```

Reemplazar `AEntidad`:

```csharp
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
```

por:

```csharp
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
        ZonaId = dto.ZonaId,
        Zona = dto.ZonaNombre is null ? null : new Zona { Id = dto.ZonaId!.Value, Nombre = dto.ZonaNombre },
        DimensionTematicaId = dto.DimensionTematicaId,
        DimensionTematica = dto.DimensionTematicaNombre is null
            ? null : new DimensionTematica { Id = dto.DimensionTematicaId!.Value, Nombre = dto.DimensionTematicaNombre },
        OrganismoResponsableId = dto.OrganismoResponsableId,
        OrganismoResponsable = dto.OrganismoResponsableNombre is null
            ? null : new OrganismoResponsable { Id = dto.OrganismoResponsableId!.Value, Nombre = dto.OrganismoResponsableNombre },
        OrigenFinanciamientoId = dto.OrigenFinanciamientoId,
        OrigenFinanciamiento = dto.OrigenFinanciamientoNombre is null
            ? null : new OrigenFinanciamiento { Id = dto.OrigenFinanciamientoId!.Value, Nombre = dto.OrigenFinanciamientoNombre },
        DocumentoAdministrativoId = dto.DocumentoAdministrativoId,
        DocumentoAdministrativo = dto.DocumentoAdministrativoNumero is null
            ? null : new DocumentoAdministrativo { Id = dto.DocumentoAdministrativoId!.Value, Numero = dto.DocumentoAdministrativoNumero },
        Notas = dto.Notas.Select(n => new NotaTarea
        {
            Id = n.Id, TareaId = dto.Id, UsuarioId = n.UsuarioId, Fecha = n.Fecha,
            Texto = n.Texto, EsAutomatica = n.EsAutomatica,
        }).ToList(),
    };
```

Agregar `using StockApp.Application.Tareas;` al principio del archivo (por `DatosClasificacionTarea`) si no está ya importado (ya está, vía `ITareaService`).

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~TareaApiClientTests"`
Expected: PASS (toda la clase).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.ApiClient/TareaApiClient.cs tests/StockApp.ApiClient.Tests/TareaApiClientTests.cs
git commit -m "feat(tareas): TareaApiClient implementa ObtenerPorIdAsync y la clasificación completa"
```

---

### Task 8: `TareaServiceFake` (UiTests) — implementa la clasificación completa

**Files:**
- Modify: `tests/StockApp.Presentation.UiTests/TareaFakes.cs`

**Interfaces:**
- Consumes: `ITareaService` completo (Task 5).
- Produces: `TareaServiceFake` compila de nuevo contra la interfaz ampliada — lo necesitan TODOS los tests de UiTests que construyen `TareaFormViewModel`/usan este fake (`TareaFormViewTests.cs`, `TareaListViewTests.cs`, `InicioPanelTareasTests.cs`).

Este task no agrega comportamiento nuevo propio (es la mecánica de "dejar compilar" antes de escribir tests de UI nuevos, Task 13) — el criterio de éxito es que la suite completa de `StockApp.Presentation.UiTests` vuelva a compilar y pasar.

- [ ] **Step 1: Correr la suite y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests`
Expected: FAIL con error de compilación — `TareaServiceFake` no implementa `ITareaService.ObtenerPorIdAsync` ni `ReclasificarAsync` ni `ListarPorDocumentoAsync`.

- [ ] **Step 2: Implementación mínima**

En `tests/StockApp.Presentation.UiTests/TareaFakes.cs`, agregar `using StockApp.Application.Tareas;` al principio del archivo si no está (ya debería estar, vía `ITareaService`).

Agregar dentro de `TareaServiceFake`, después de `public List<(int Id, PrioridadTarea Prioridad)> CambiosDePrioridad { get; } = new();`:

```csharp
    public List<(int Id, DatosClasificacionTarea Datos)> Reclasificaciones { get; } = new();
```

Agregar al final de la clase `TareaServiceFake` (antes del `}` que la cierra, después de `AgregarNotaAsync`):

```csharp

    public Task<Tarea?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_tareas.FirstOrDefault(t => t.Id == id));

    public Task ReclasificarAsync(int tareaId, DatosClasificacionTarea datos)
    {
        var tarea = _tareas.First(t => t.Id == tareaId);
        tarea.ZonaId = datos.ZonaId;
        tarea.DimensionTematicaId = datos.DimensionTematicaId;
        tarea.OrganismoResponsableId = datos.OrganismoResponsableId;
        tarea.OrigenFinanciamientoId = datos.OrigenFinanciamientoId;
        tarea.DocumentoAdministrativoId = datos.DocumentoAdministrativoId;
        Reclasificaciones.Add((tareaId, datos));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId) =>
        Task.FromResult<IReadOnlyList<Tarea>>(_tareas.Where(t => t.DocumentoAdministrativoId == documentoId).ToList());
```

- [ ] **Step 3: Correr la suite y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.UiTests`
Expected: PASS (toda la suite compila y corre verde — todavía sin tests nuevos que ejerciten `ReclasificarAsync`/`ListarPorDocumentoAsync`, eso llega en Tasks 12-14).

- [ ] **Step 4: Commit**
```bash
git add tests/StockApp.Presentation.UiTests/TareaFakes.cs
git commit -m "test(tareas): TareaServiceFake implementa ObtenerPorIdAsync, ReclasificarAsync y ListarPorDocumentoAsync"
```

---

### Task 9: `ClasificacionTareaPanelViewModel` + `ClasificacionTareaPanelView` — panel reusable

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Tareas/ClasificacionTareaPanelViewModel.cs`
- Create: `src/StockApp.Presentation/Views/Tareas/ClasificacionTareaPanelView.axaml`
- Create: `src/StockApp.Presentation/Views/Tareas/ClasificacionTareaPanelView.axaml.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Tareas/ClasificacionTareaPanelViewModelTests.cs`

**Interfaces:**
- Consumes: `IZonaService`/`IDimensionTematicaService`/`IOrganismoResponsableService`/`IOrigenFinanciamientoService` (Plan A, `ListarActivasAsync()`), `IDocumentoAdministrativoService.ListarActivosAsync(FiltroDocumentos)`/`ObtenerPorIdAsync(int)`.
- Produces: `ClasificacionTareaPanelViewModel` — se embebe tanto en `TareaFormViewModel` (Task 12, alta inline) como en `ReclasificarTareaDialogViewModel` (Task 10, modal), evitando duplicar la lógica de combos + buscador en los dos lugares (mismo criterio que `AdjuntosDocumentoPanelViewModel`/`AdjuntosDocumentoPanelView`).

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.Tests/ViewModels/Tareas/ClasificacionTareaPanelViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Documentos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

public class ClasificacionTareaPanelViewModelTests
{
    private static (ClasificacionTareaPanelViewModel Vm, Mock<IZonaService> Zonas,
                     Mock<IDimensionTematicaService> Dimensiones, Mock<IOrganismoResponsableService> Organismos,
                     Mock<IOrigenFinanciamientoService> Origenes, Mock<IDocumentoAdministrativoService> Documentos)
        Crear()
    {
        var zonas = new Mock<IZonaService>();
        var dimensiones = new Mock<IDimensionTematicaService>();
        var organismos = new Mock<IOrganismoResponsableService>();
        var origenes = new Mock<IOrigenFinanciamientoService>();
        var documentos = new Mock<IDocumentoAdministrativoService>();

        zonas.Setup(z => z.ListarActivasAsync()).ReturnsAsync(new List<Zona>
        {
            new() { Id = 1, Nombre = "Centro" }, new() { Id = 2, Nombre = "Norte" },
        });
        dimensiones.Setup(d => d.ListarActivasAsync()).ReturnsAsync(new List<DimensionTematica>
        {
            new() { Id = 3, Nombre = "Tránsito" },
        });
        organismos.Setup(o => o.ListarActivasAsync()).ReturnsAsync(new List<OrganismoResponsable>
        {
            new() { Id = 4, Nombre = "Intendencia" },
        });
        origenes.Setup(o => o.ListarActivasAsync()).ReturnsAsync(new List<OrigenFinanciamiento>
        {
            new() { Id = 5, Nombre = "Presupuesto propio" },
        });

        var vm = new ClasificacionTareaPanelViewModel(
            zonas.Object, dimensiones.Object, organismos.Object, origenes.Object, documentos.Object);
        return (vm, zonas, dimensiones, organismos, origenes, documentos);
    }

    [Fact]
    public async Task InicializarAsync_SinValoresActuales_PopulaLosCuatroCatalogosSinSeleccion()
    {
        var ctx = Crear();

        await ctx.Vm.InicializarAsync();

        Assert.Equal(2, ctx.Vm.ZonasDisponibles.Count);
        Assert.Single(ctx.Vm.DimensionesDisponibles);
        Assert.Single(ctx.Vm.OrganismosDisponibles);
        Assert.Single(ctx.Vm.OrigenesDisponibles);
        Assert.Null(ctx.Vm.ZonaSeleccionada);
        Assert.Null(ctx.Vm.DocumentoSeleccionado);
    }

    [Fact]
    public async Task InicializarAsync_ConValoresActuales_PrecargaLaSeleccion()
    {
        var ctx = Crear();
        ctx.Documentos.Setup(d => d.ObtenerPorIdAsync(9)).ReturnsAsync(new DocumentoAdministrativo
        {
            Id = 9, Numero = "0001", Anio = 2026, Tipo = TipoDocumento.Expediente, Descripcion = "x",
        });

        await ctx.Vm.InicializarAsync(new DatosClasificacionTarea(1, 3, null, null, 9));

        Assert.Equal(1, ctx.Vm.ZonaSeleccionada?.Id);
        Assert.Equal(3, ctx.Vm.DimensionSeleccionada?.Id);
        Assert.Null(ctx.Vm.OrganismoSeleccionado);
        Assert.Equal(9, ctx.Vm.DocumentoSeleccionado?.Id);
    }

    [Fact]
    public void ObtenerDatos_SinSeleccion_DevuelveLosCincoCamposEnNull()
    {
        var ctx = Crear();

        var datos = ctx.Vm.ObtenerDatos();

        Assert.Equal(new DatosClasificacionTarea(null, null, null, null, null), datos);
    }

    [Fact]
    public async Task ObtenerDatos_ConSeleccion_ReflejaLosIdsSeleccionados()
    {
        var ctx = Crear();
        await ctx.Vm.InicializarAsync();
        ctx.Vm.ZonaSeleccionada = ctx.Vm.ZonasDisponibles.First(z => z.Id == 2);

        var datos = ctx.Vm.ObtenerDatos();

        Assert.Equal(2, datos.ZonaId);
        Assert.Null(datos.DimensionTematicaId);
    }

    [Fact]
    public async Task BuscarExpedientesAsync_TextoCortoDelMinimo_NoConsultaAlServicio()
    {
        var ctx = Crear();

        var resultado = await ctx.Vm.BuscarExpedientesAsync("ab", CancellationToken.None);

        Assert.Empty(resultado);
        ctx.Documentos.Verify(d => d.ListarActivosAsync(It.IsAny<FiltroDocumentos>()), Times.Never);
    }

    [Fact]
    public async Task BuscarExpedientesAsync_TextoValido_FiltraPorTipoExpediente()
    {
        var ctx = Crear();
        ctx.Documentos.Setup(d => d.ListarActivosAsync(It.IsAny<FiltroDocumentos>()))
            .ReturnsAsync(new List<DocumentoAdministrativo>
            {
                new() { Id = 1, Numero = "0001", Tipo = TipoDocumento.Expediente, Descripcion = "x" },
            });

        var resultado = await ctx.Vm.BuscarExpedientesAsync("obra", CancellationToken.None);

        Assert.Single(resultado);
        ctx.Documentos.Verify(d => d.ListarActivosAsync(
            It.Is<FiltroDocumentos>(f => f.Tipo == TipoDocumento.Expediente && f.Texto == "obra")), Times.Once);
    }

    [Fact]
    public async Task BuscarExpedientesAsync_MasResultadosQueElTope_AcotaYAvisa()
    {
        var ctx = Crear();
        var muchos = Enumerable.Range(1, 25)
            .Select(i => new DocumentoAdministrativo { Id = i, Numero = $"{i:0000}", Tipo = TipoDocumento.Expediente, Descripcion = "x" })
            .ToList();
        ctx.Documentos.Setup(d => d.ListarActivosAsync(It.IsAny<FiltroDocumentos>())).ReturnsAsync(muchos);

        var resultado = await ctx.Vm.BuscarExpedientesAsync("expediente", CancellationToken.None);

        Assert.Equal(20, resultado.Count());
        Assert.NotNull(ctx.Vm.MensajeBuscadorExpediente);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ClasificacionTareaPanelViewModelTests"`
Expected: FAIL con error de compilación — `ClasificacionTareaPanelViewModel` no existe.

- [ ] **Step 3: Implementación mínima**

Crear `src/StockApp.Presentation/ViewModels/Tareas/ClasificacionTareaPanelViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.ViewModels.Tareas;

/// <summary>
/// Panel reusable de los cinco clasificadores de una tarea (spec 2026-09-08): cuatro combos
/// (Zona/Dimensión/Organismo/Origen) + buscador de expediente server-side (D22, molde de
/// MovimientoHistorialViewModel.BuscarProductosAsync). Se embebe SIN modificar en dos
/// lugares — alta inline (TareaFormViewModel, Task 12) y el modal de reclasificación
/// (ReclasificarTareaDialogViewModel, Task 10) — mismo criterio de composición que
/// AdjuntosDocumentoPanelViewModel/AdjuntosDocumentoPanelView.
/// </summary>
public partial class ClasificacionTareaPanelViewModel : ViewModelBase
{
    private const int MinimoCaracteresBusquedaExpediente = 3;
    private const int TopeResultadosBusquedaExpediente = 20;

    private readonly IZonaService _zonasService;
    private readonly IDimensionTematicaService _dimensionesService;
    private readonly IOrganismoResponsableService _organismosService;
    private readonly IOrigenFinanciamientoService _origenesService;
    private readonly IDocumentoAdministrativoService _documentosService;

    public ObservableCollection<Zona> ZonasDisponibles { get; } = new();
    public ObservableCollection<DimensionTematica> DimensionesDisponibles { get; } = new();
    public ObservableCollection<OrganismoResponsable> OrganismosDisponibles { get; } = new();
    public ObservableCollection<OrigenFinanciamiento> OrigenesDisponibles { get; } = new();

    [ObservableProperty] private Zona? _zonaSeleccionada;
    [ObservableProperty] private DimensionTematica? _dimensionSeleccionada;
    [ObservableProperty] private OrganismoResponsable? _organismoSeleccionado;
    [ObservableProperty] private OrigenFinanciamiento? _origenSeleccionado;
    [ObservableProperty] private DocumentoAdministrativo? _documentoSeleccionado;
    [ObservableProperty] private string? _mensajeBuscadorExpediente;

    /// <summary>Delegado para AutoCompleteBox.AsyncPopulator (D22 del spec: mínimo de
    /// caracteres antes de disparar la búsqueda y tope de resultados con aviso — mismo
    /// motivo que MovimientoHistorialViewModel.BuscarProductosAsync: ListarActivosAsync NO
    /// pagina ni limita).</summary>
    public Func<string?, CancellationToken, Task<IEnumerable<object>>> BuscarExpedientesAsync { get; }

    public ClasificacionTareaPanelViewModel(
        IZonaService zonasService, IDimensionTematicaService dimensionesService,
        IOrganismoResponsableService organismosService, IOrigenFinanciamientoService origenesService,
        IDocumentoAdministrativoService documentosService)
    {
        _zonasService = zonasService;
        _dimensionesService = dimensionesService;
        _organismosService = organismosService;
        _origenesService = origenesService;
        _documentosService = documentosService;
        BuscarExpedientesAsync = BuscarExpedientesInternalAsync;
    }

    /// <summary>Puebla los cuatro catálogos y, si <paramref name="actual"/> no es null,
    /// precarga la selección (usado por el modal de reclasificación, Task 10 — D21: lo que
    /// el Admin ve precargado es exactamente lo que queda guardado si no toca nada).</summary>
    public async Task InicializarAsync(DatosClasificacionTarea? actual = null)
    {
        var zonas = await _zonasService.ListarActivasAsync();
        ZonasDisponibles.Clear();
        foreach (var z in zonas) ZonasDisponibles.Add(z);

        var dimensiones = await _dimensionesService.ListarActivasAsync();
        DimensionesDisponibles.Clear();
        foreach (var d in dimensiones) DimensionesDisponibles.Add(d);

        var organismos = await _organismosService.ListarActivasAsync();
        OrganismosDisponibles.Clear();
        foreach (var o in organismos) OrganismosDisponibles.Add(o);

        var origenes = await _origenesService.ListarActivasAsync();
        OrigenesDisponibles.Clear();
        foreach (var o in origenes) OrigenesDisponibles.Add(o);

        ZonaSeleccionada = null;
        DimensionSeleccionada = null;
        OrganismoSeleccionado = null;
        OrigenSeleccionado = null;
        DocumentoSeleccionado = null;
        MensajeBuscadorExpediente = null;

        if (actual is null) return;

        ZonaSeleccionada = ZonasDisponibles.FirstOrDefault(z => z.Id == actual.ZonaId);
        DimensionSeleccionada = DimensionesDisponibles.FirstOrDefault(d => d.Id == actual.DimensionTematicaId);
        OrganismoSeleccionado = OrganismosDisponibles.FirstOrDefault(o => o.Id == actual.OrganismoResponsableId);
        OrigenSeleccionado = OrigenesDisponibles.FirstOrDefault(o => o.Id == actual.OrigenFinanciamientoId);
        if (actual.DocumentoAdministrativoId is int documentoId)
            DocumentoSeleccionado = await _documentosService.ObtenerPorIdAsync(documentoId);
    }

    private async Task<IEnumerable<object>> BuscarExpedientesInternalAsync(string? texto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(texto) || texto.Trim().Length < MinimoCaracteresBusquedaExpediente)
        {
            MensajeBuscadorExpediente = null;
            return Array.Empty<object>();
        }

        var filtro = new FiltroDocumentos(TipoDocumento.Expediente, null, texto, null);
        var resultados = await _documentosService.ListarActivosAsync(filtro);

        if (resultados.Count > TopeResultadosBusquedaExpediente)
        {
            MensajeBuscadorExpediente = "Hay más resultados de los que se muestran. Refiná la búsqueda.";
            return resultados.Take(TopeResultadosBusquedaExpediente).Cast<object>();
        }

        MensajeBuscadorExpediente = null;
        return resultados.Cast<object>();
    }

    /// <summary>Reemplazo total (D21): los cinco campos SIEMPRE reflejan la selección
    /// actual, incluido null como desasignación explícita.</summary>
    public DatosClasificacionTarea ObtenerDatos() => new(
        ZonaSeleccionada?.Id, DimensionSeleccionada?.Id, OrganismoSeleccionado?.Id,
        OrigenSeleccionado?.Id, DocumentoSeleccionado?.Id);
}
```

Crear `src/StockApp.Presentation/Views/Tareas/ClasificacionTareaPanelView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Tareas"
             xmlns:dom="using:StockApp.Domain.Entities"
             xmlns:c="using:StockApp.Presentation.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="420"
             x:Class="StockApp.Presentation.Views.Tareas.ClasificacionTareaPanelView"
             x:DataType="vm:ClasificacionTareaPanelViewModel">

    <StackPanel Spacing="{DynamicResource Espacio3}">

        <c:CampoFormulario Etiqueta="Zona (opcional)">
            <ComboBox ItemsSource="{Binding ZonasDisponibles}"
                      SelectedItem="{Binding ZonaSeleccionada}"
                      PlaceholderText="Elegí la zona"
                      HorizontalAlignment="Stretch">
                <ComboBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding Nombre}" />
                    </DataTemplate>
                </ComboBox.ItemTemplate>
            </ComboBox>
        </c:CampoFormulario>

        <c:CampoFormulario Etiqueta="Dimensión (opcional)">
            <ComboBox ItemsSource="{Binding DimensionesDisponibles}"
                      SelectedItem="{Binding DimensionSeleccionada}"
                      PlaceholderText="Elegí la dimensión"
                      HorizontalAlignment="Stretch">
                <ComboBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding Nombre}" />
                    </DataTemplate>
                </ComboBox.ItemTemplate>
            </ComboBox>
        </c:CampoFormulario>

        <c:CampoFormulario Etiqueta="Organismo responsable (opcional)">
            <ComboBox ItemsSource="{Binding OrganismosDisponibles}"
                      SelectedItem="{Binding OrganismoSeleccionado}"
                      PlaceholderText="Elegí el organismo"
                      HorizontalAlignment="Stretch">
                <ComboBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding Nombre}" />
                    </DataTemplate>
                </ComboBox.ItemTemplate>
            </ComboBox>
        </c:CampoFormulario>

        <c:CampoFormulario Etiqueta="Origen de financiamiento (opcional)">
            <ComboBox ItemsSource="{Binding OrigenesDisponibles}"
                      SelectedItem="{Binding OrigenSeleccionado}"
                      PlaceholderText="Elegí el origen"
                      HorizontalAlignment="Stretch">
                <ComboBox.ItemTemplate>
                    <DataTemplate>
                        <TextBlock Text="{Binding Nombre}" />
                    </DataTemplate>
                </ComboBox.ItemTemplate>
            </ComboBox>
        </c:CampoFormulario>

        <c:CampoFormulario Etiqueta="Expediente (opcional)">
            <StackPanel Spacing="4">
                <AutoCompleteBox Watermark="Buscar expediente por número o descripción..."
                                  AsyncPopulator="{Binding BuscarExpedientesAsync}"
                                  SelectedItem="{Binding DocumentoSeleccionado}"
                                  ValueMemberBinding="{Binding Numero, DataType={x:Type dom:DocumentoAdministrativo}}"
                                  FilterMode="None"
                                  MinimumPopulateDelay="0:0:0.3"
                                  HorizontalAlignment="Stretch">
                    <AutoCompleteBox.ItemTemplate>
                        <DataTemplate x:DataType="dom:DocumentoAdministrativo">
                            <TextBlock Text="{Binding Numero, StringFormat='Expediente {0}'}" />
                        </DataTemplate>
                    </AutoCompleteBox.ItemTemplate>
                </AutoCompleteBox>
                <TextBlock Text="{Binding MensajeBuscadorExpediente}"
                           Classes="caption"
                           Foreground="{DynamicResource TextoTerciarioBrush}"
                           IsVisible="{Binding MensajeBuscadorExpediente, Converter={x:Static ObjectConverters.IsNotNull}}" />
            </StackPanel>
        </c:CampoFormulario>

    </StackPanel>

</UserControl>
```

Crear `src/StockApp.Presentation/Views/Tareas/ClasificacionTareaPanelView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Tareas;

/// <summary>
/// Sin DataContextChanged propio a propósito: quien la embebe (TareaFormView, Task 13;
/// ReclasificarTareaDialog, Task 10) es responsable de llamar
/// ClasificacionTareaPanelViewModel.InicializarAsync() en su propio punto de carga — mismo
/// criterio que AdjuntosDocumentoPanelView, que tampoco se auto-inicializa.
/// </summary>
public partial class ClasificacionTareaPanelView : UserControl
{
    public ClasificacionTareaPanelView()
    {
        InitializeComponent();
    }
}
```

En `src/StockApp.Presentation/App.axaml.cs`, ubicar la línea (sección "Módulo Tareas"):

```csharp
        services.AddTransient<TareaListViewModel>();
        services.AddTransient<TareaFormViewModel>();
```

y reemplazarla por:

```csharp
        services.AddTransient<TareaListViewModel>();
        services.AddTransient<ClasificacionTareaPanelViewModel>();
        services.AddTransient<TareaFormViewModel>();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ClasificacionTareaPanelViewModelTests"`
Expected: PASS (los 7 tests).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Tareas/ClasificacionTareaPanelViewModel.cs src/StockApp.Presentation/Views/Tareas/ClasificacionTareaPanelView.axaml src/StockApp.Presentation/Views/Tareas/ClasificacionTareaPanelView.axaml.cs src/StockApp.Presentation/App.axaml.cs tests/StockApp.Presentation.Tests/ViewModels/Tareas/ClasificacionTareaPanelViewModelTests.cs
git commit -m "feat(tareas): panel reusable de clasificación (combos + buscador de expediente)"
```

---

### Task 10: `IClasificacionTareaDialogService` + `ReclasificarTareaDialog` — modal de reclasificación

**Files:**
- Create: `src/StockApp.Presentation/Services/IClasificacionTareaDialogService.cs`
- Create: `src/StockApp.Presentation/Services/ClasificacionTareaDialogService.cs`
- Create: `src/StockApp.Presentation/ViewModels/Tareas/ReclasificarTareaDialogViewModel.cs`
- Create: `src/StockApp.Presentation/Views/Tareas/ReclasificarTareaDialog.axaml`
- Create: `src/StockApp.Presentation/Views/Tareas/ReclasificarTareaDialog.axaml.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.Tests/Services/ClasificacionTareaDialogServiceTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Tareas/ReclasificarTareaDialogViewModelTests.cs`

**Interfaces:**
- Consumes: `ClasificacionTareaPanelViewModel` (Task 9), `IZonaService`/`IDimensionTematicaService`/`IOrganismoResponsableService`/`IOrigenFinanciamientoService`/`IDocumentoAdministrativoService` (para construir el panel del diálogo).
- Produces: `IClasificacionTareaDialogService.PedirClasificacionAsync(DatosClasificacionTarea actual) : Task<DatosClasificacionTarea?>` — lo consume Task 12 (`TareaFormViewModel.ReclasificarCommand`).

Primer diálogo con formulario real de la app (los tres diálogos existentes en `Views/Dialogs/` solo piden texto libre o sí/no) — el modal se ubica en `Views/Tareas/` (no en `Views/Dialogs/`) por cohesión de módulo, mismo criterio que `AdjuntosDocumentoPanelView` vive en `Views/Documentos/`.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.Tests/Services/ClasificacionTareaDialogServiceTests.cs`:

```csharp
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Tareas;
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

/// <summary>
/// Mismo criterio defensivo que ConfirmacionServiceTests: sin Avalonia.Application.Current
/// inicializado (tests headless de este proyecto, sin [AvaloniaFact]), PedirClasificacionAsync
/// debe resolver sin excepción devolviendo null (equivale a cancelar), no intentar abrir una
/// ventana real.
/// </summary>
public class ClasificacionTareaDialogServiceTests
{
    [Fact]
    public async Task PedirClasificacionAsync_SinAppAvalonia_DevuelveNull()
    {
        var svc = new ClasificacionTareaDialogService(
            Mock.Of<IZonaService>(), Mock.Of<IDimensionTematicaService>(),
            Mock.Of<IOrganismoResponsableService>(), Mock.Of<IOrigenFinanciamientoService>(),
            Mock.Of<IDocumentoAdministrativoService>());

        var resultado = await svc.PedirClasificacionAsync(new DatosClasificacionTarea(null, null, null, null, null));

        Assert.Null(resultado);
    }

    [Fact]
    public void IClasificacionTareaDialogService_EsMockeable()
    {
        var mock = new Mock<IClasificacionTareaDialogService>();
        mock.Setup(s => s.PedirClasificacionAsync(It.IsAny<DatosClasificacionTarea>()))
            .ReturnsAsync(new DatosClasificacionTarea(1, null, null, null, null));

        Assert.NotNull(mock.Object);
    }
}
```

Crear `tests/StockApp.Presentation.Tests/ViewModels/Tareas/ReclasificarTareaDialogViewModelTests.cs`:

```csharp
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

public class ReclasificarTareaDialogViewModelTests
{
    [Fact]
    public void Constructor_ExponeElPanelRecibido()
    {
        var panel = new ClasificacionTareaPanelViewModel(
            Mock.Of<IZonaService>(), Mock.Of<IDimensionTematicaService>(),
            Mock.Of<IOrganismoResponsableService>(), Mock.Of<IOrigenFinanciamientoService>(),
            Mock.Of<IDocumentoAdministrativoService>());

        var vm = new ReclasificarTareaDialogViewModel(panel);

        Assert.Same(panel, vm.Panel);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ClasificacionTareaDialogServiceTests|FullyQualifiedName~ReclasificarTareaDialogViewModelTests"`
Expected: FAIL con error de compilación — `IClasificacionTareaDialogService`/`ClasificacionTareaDialogService`/`ReclasificarTareaDialogViewModel` no existen.

- [ ] **Step 3: Implementación mínima**

Crear `src/StockApp.Presentation/Services/IClasificacionTareaDialogService.cs`:

```csharp
using System.Threading.Tasks;
using StockApp.Application.Tareas;

namespace StockApp.Presentation.Services;

/// <summary>
/// Diálogo modal de reclasificación (spec 2026-09-08, D9/D21). A diferencia de
/// IConfirmacionService (texto libre o sí/no), este pide un formulario completo — mismos
/// cuatro combos y buscador de expediente que el alta, precargados con los valores actuales
/// de la tarea (Presentation.ViewModels.Tareas.ClasificacionTareaPanelViewModel).
/// </summary>
public interface IClasificacionTareaDialogService
{
    /// <summary>Muestra el modal precargado con <paramref name="actual"/>. Devuelve los
    /// datos elegidos si el Admin confirma, o null si canceló.</summary>
    Task<DatosClasificacionTarea?> PedirClasificacionAsync(DatosClasificacionTarea actual);
}
```

Crear `src/StockApp.Presentation/Services/ClasificacionTareaDialogService.cs`:

```csharp
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Tareas;
using StockApp.Presentation.ViewModels.Tareas;
using StockApp.Presentation.Views.Tareas;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de IClasificacionTareaDialogService. Mismo mecanismo defensivo que
/// ConfirmacionService (Application.Current/MainWindow pueden faltar en tests headless) — a
/// diferencia de ConfirmacionService, acá el panel se construye a mano con los servicios de
/// catálogo (no se resuelve por contenedor DI: mismo criterio que ConfirmacionService
/// "new"-ea sus diálogos en vez de pedirlos al ServiceProvider).
/// </summary>
public class ClasificacionTareaDialogService : IClasificacionTareaDialogService
{
    private readonly IZonaService _zonasService;
    private readonly IDimensionTematicaService _dimensionesService;
    private readonly IOrganismoResponsableService _organismosService;
    private readonly IOrigenFinanciamientoService _origenesService;
    private readonly IDocumentoAdministrativoService _documentosService;

    public ClasificacionTareaDialogService(
        IZonaService zonasService, IDimensionTematicaService dimensionesService,
        IOrganismoResponsableService organismosService, IOrigenFinanciamientoService origenesService,
        IDocumentoAdministrativoService documentosService)
    {
        _zonasService = zonasService;
        _dimensionesService = dimensionesService;
        _organismosService = organismosService;
        _origenesService = origenesService;
        _documentosService = documentosService;
    }

    public Task<DatosClasificacionTarea?> PedirClasificacionAsync(DatosClasificacionTarea actual)
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<DatosClasificacionTarea?>(null);

        return Dispatcher.UIThread.InvokeAsync(() => MostrarDialogoAsync(actual));
    }

    private async Task<DatosClasificacionTarea?> MostrarDialogoAsync(DatosClasificacionTarea actual)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var owner = lifetime?.MainWindow;
        if (owner is null) return null;

        var panel = new ClasificacionTareaPanelViewModel(
            _zonasService, _dimensionesService, _organismosService, _origenesService, _documentosService);
        await panel.InicializarAsync(actual);

        var vm = new ReclasificarTareaDialogViewModel(panel);
        var dialog = new ReclasificarTareaDialog { DataContext = vm };
        return await dialog.ShowDialog<DatosClasificacionTarea?>(owner);
    }
}
```

Crear `src/StockApp.Presentation/ViewModels/Tareas/ReclasificarTareaDialogViewModel.cs`:

```csharp
namespace StockApp.Presentation.ViewModels.Tareas;

/// <summary>
/// ViewModel del modal de reclasificación: envoltorio delgado sobre ClasificacionTareaPanelViewModel
/// (Task 9) — el diálogo en sí no tiene estado propio, Aceptar/Cancelar viven en el
/// code-behind de ReclasificarTareaDialog (mismo criterio que PedirTextoDialog).
/// </summary>
public partial class ReclasificarTareaDialogViewModel : ViewModelBase
{
    public ClasificacionTareaPanelViewModel Panel { get; }

    public ReclasificarTareaDialogViewModel(ClasificacionTareaPanelViewModel panel)
    {
        Panel = panel;
    }
}
```

Crear `src/StockApp.Presentation/Views/Tareas/ReclasificarTareaDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:StockApp.Presentation.ViewModels.Tareas"
        xmlns:pnl="using:StockApp.Presentation.Views.Tareas"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d" d:DesignWidth="480" d:DesignHeight="560"
        x:Class="StockApp.Presentation.Views.Tareas.ReclasificarTareaDialog"
        x:DataType="vm:ReclasificarTareaDialogViewModel"
        Title="Reclasificar tarea"
        Width="480"
        SizeToContent="Height"
        WindowStartupLocation="CenterOwner"
        CanResize="False"
        ShowInTaskbar="False">

    <Border Padding="{DynamicResource MargenVista}">
        <StackPanel Spacing="{DynamicResource Espacio3}">

            <TextBlock Text="Elegí los clasificadores. Dejar un campo vacío lo desasigna." Classes="body" TextWrapping="Wrap" />

            <pnl:ClasificacionTareaPanelView DataContext="{Binding Panel}" />

            <StackPanel Orientation="Horizontal"
                        HorizontalAlignment="Right"
                        Spacing="{DynamicResource Espacio2}"
                        Margin="0,8,0,0">

                <Button x:Name="CancelarButton"
                        Content="Cancelar"
                        Classes="secondary"
                        Width="100"
                        Click="OnCancelarClick" />

                <Button x:Name="AceptarButton"
                        Content="Aceptar"
                        Classes="primary"
                        Width="100"
                        Click="OnAceptarClick" />

            </StackPanel>

        </StackPanel>
    </Border>

</Window>
```

Crear `src/StockApp.Presentation/Views/Tareas/ReclasificarTareaDialog.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.Views.Tareas;

/// <summary>
/// Diálogo modal con formulario real (spec 2026-09-08) — molde de PedirTextoDialog:
/// "Aceptar" cierra devolviendo DatosClasificacionTarea (leído del panel embebido);
/// "Cancelar" devuelve null. Usá Window.ShowDialog&lt;TResult&gt; con
/// TResult=DatosClasificacionTarea? para obtener el resultado.
/// </summary>
public partial class ReclasificarTareaDialog : Window
{
    public ReclasificarTareaDialog()
    {
        InitializeComponent();
    }

    private void OnAceptarClick(object? sender, RoutedEventArgs e)
    {
        var vm = (ReclasificarTareaDialogViewModel)DataContext!;
        Close(vm.Panel.ObtenerDatos());
    }

    private void OnCancelarClick(object? sender, RoutedEventArgs e) => Close(null);
}
```

En `src/StockApp.Presentation/App.axaml.cs`, ubicar la línea:

```csharp
        services.AddSingleton<IConfirmacionService, ConfirmacionService>();
```

y agregar inmediatamente después:

```csharp
        services.AddSingleton<IClasificacionTareaDialogService, ClasificacionTareaDialogService>();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ClasificacionTareaDialogServiceTests|FullyQualifiedName~ReclasificarTareaDialogViewModelTests"`
Expected: PASS (los 3 tests).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/Services/IClasificacionTareaDialogService.cs src/StockApp.Presentation/Services/ClasificacionTareaDialogService.cs src/StockApp.Presentation/ViewModels/Tareas/ReclasificarTareaDialogViewModel.cs src/StockApp.Presentation/Views/Tareas/ReclasificarTareaDialog.axaml src/StockApp.Presentation/Views/Tareas/ReclasificarTareaDialog.axaml.cs src/StockApp.Presentation/App.axaml.cs tests/StockApp.Presentation.Tests/Services/ClasificacionTareaDialogServiceTests.cs tests/StockApp.Presentation.Tests/ViewModels/Tareas/ReclasificarTareaDialogViewModelTests.cs
git commit -m "feat(tareas): modal de reclasificación con formulario real"
```

---

### Task 11: fakes de UiTests para los 4 catálogos + el diálogo de clasificación

**Files:**
- Modify: `tests/StockApp.Presentation.UiTests/TareaFakes.cs`

**Interfaces:**
- Produces: `ZonaServiceFake`, `DimensionTematicaServiceFake`, `OrganismoResponsableServiceFake`, `OrigenFinanciamientoServiceFake`, `ClasificacionDialogServiceFake` — los consumen Tasks 12-14. Se crean acá (auto-contenidos en este plan) en vez de asumir que el Plan A dejó fakes de UI con estos nombres exactos: el Plan A solo garantiza entidades/servicios de producción, no fixtures de test de otro plan.

- [ ] **Step 1: Escribir el test que falla**

Agregar al final de `tests/StockApp.Presentation.UiTests/TareaFakes.cs` (después de la clase `NavigationRecorderFake`, antes del cierre del archivo si lo hubiera — el archivo termina con la clase, así que se agrega a continuación):

```csharp

/// <summary>Solo ListarActivasAsync tiene comportamiento real (lo que consume
/// ClasificacionTareaPanelViewModel) — el resto de ICategoriaService no lo ejercita ningún
/// test de este plan, mismo criterio que AuthServiceFake.LoginAsync/LogoutAsync.</summary>
internal sealed class ZonaServiceFake : IZonaService
{
    private readonly List<Zona> _activas;
    public ZonaServiceFake(List<Zona>? activas = null) => _activas = activas ?? new List<Zona>();
    public Task<IReadOnlyList<Zona>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<Zona>>(_activas);
    public Task<int> AltaAsync(Zona zona) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(Zona zona) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<Zona>> ListarTodasAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");
}

internal sealed class DimensionTematicaServiceFake : IDimensionTematicaService
{
    private readonly List<DimensionTematica> _activas;
    public DimensionTematicaServiceFake(List<DimensionTematica>? activas = null) => _activas = activas ?? new List<DimensionTematica>();
    public Task<IReadOnlyList<DimensionTematica>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<DimensionTematica>>(_activas);
    public Task<int> AltaAsync(DimensionTematica d) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(DimensionTematica d) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");
}

internal sealed class OrganismoResponsableServiceFake : IOrganismoResponsableService
{
    private readonly List<OrganismoResponsable> _activas;
    public OrganismoResponsableServiceFake(List<OrganismoResponsable>? activas = null) => _activas = activas ?? new List<OrganismoResponsable>();
    public Task<IReadOnlyList<OrganismoResponsable>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<OrganismoResponsable>>(_activas);
    public Task<int> AltaAsync(OrganismoResponsable o) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(OrganismoResponsable o) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");
}

internal sealed class OrigenFinanciamientoServiceFake : IOrigenFinanciamientoService
{
    private readonly List<OrigenFinanciamiento> _activas;
    public OrigenFinanciamientoServiceFake(List<OrigenFinanciamiento>? activas = null) => _activas = activas ?? new List<OrigenFinanciamiento>();
    public Task<IReadOnlyList<OrigenFinanciamiento>> ListarActivasAsync() => Task.FromResult<IReadOnlyList<OrigenFinanciamiento>>(_activas);
    public Task<int> AltaAsync(OrigenFinanciamiento o) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(OrigenFinanciamiento o) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync() => throw new NotSupportedException("No usado en este banco de pruebas.");
}

/// <summary>Fake del diálogo de reclasificación (Task 10): en vez de abrir una Window real,
/// devuelve ResultadoADevolver y graba lo que recibió — mismo criterio que
/// NavigationRecorderFake para verificar SIN levantar el diálogo real.</summary>
internal sealed class ClasificacionDialogServiceFake : IClasificacionTareaDialogService
{
    public DatosClasificacionTarea? ResultadoADevolver { get; set; }
    public DatosClasificacionTarea? UltimoActualRecibido { get; private set; }
    public int Llamadas { get; private set; }

    public Task<DatosClasificacionTarea?> PedirClasificacionAsync(DatosClasificacionTarea actual)
    {
        Llamadas++;
        UltimoActualRecibido = actual;
        return Task.FromResult(ResultadoADevolver);
    }
}
```

Agregar los `using` que falten al principio de `TareaFakes.cs`:

```csharp
using StockApp.Application.Catalogo;
using StockApp.Presentation.Services;
```

- [ ] **Step 2: Correr la suite y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests`
Expected: FAIL con error de compilación — `IZonaService`/`IDimensionTematicaService`/`IOrganismoResponsableService`/`IOrigenFinanciamientoService`/`IClasificacionTareaDialogService` no resuelven, o `Zona`/`DimensionTematica`/`OrganismoResponsable`/`OrigenFinanciamiento` no resuelven (si el `using StockApp.Domain.Entities;` no está — ya debería estar, vía `Tarea`/`Usuario`).

- [ ] **Step 3: Correr la suite y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.UiTests`
Expected: PASS (compila y corre verde — estos fakes todavía no los usa ningún test, eso llega en Tasks 12-14).

- [ ] **Step 4: Commit**
```bash
git add tests/StockApp.Presentation.UiTests/TareaFakes.cs
git commit -m "test(tareas): fakes de UiTests para los catálogos y el diálogo de clasificación"
```

---

### Task 12: `TareaFormViewModel` — alta con clasificación, detalle de solo lectura, comando Reclasificar

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Tareas/TareaFormViewModel.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFormViewModelTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/TareaFormViewTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/InicioPanelTareasTests.cs`

**Interfaces:**
- Consumes: `ClasificacionTareaPanelViewModel` (Task 9), `IClasificacionTareaDialogService` (Task 10).
- Produces: `TareaFormViewModel` con constructor extendido (2 params nuevos) — rompe los 4 sitios de construcción existentes, todos actualizados en este mismo task.

- [ ] **Step 1: Actualizar los 4 sitios de construcción existentes (mecánico, deja compilar)**

En `tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFormViewModelTests.cs`, reemplazar el helper `Crear`:

```csharp
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
```

por:

```csharp
    private static (TareaFormViewModel Vm, Mock<ITareaService> Svc, Mock<IConfirmacionService> Confirm,
                     Mock<IClasificacionTareaDialogService> DialogoClasificacion)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var svc = new Mock<ITareaService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var dialogoClasificacion = new Mock<IClasificacionTareaDialogService>();

        var panel = new ClasificacionTareaPanelViewModel(
            Mock.Of<IZonaService>(), Mock.Of<IDimensionTematicaService>(),
            Mock.Of<IOrganismoResponsableService>(), Mock.Of<IOrigenFinanciamientoService>(),
            Mock.Of<IDocumentoAdministrativoService>());

        var vm = new TareaFormViewModel(svc.Object, session.Object, nav.Object, confirm.Object, panel, dialogoClasificacion.Object);
        return (vm, svc, confirm, dialogoClasificacion);
    }
```

Agregar los `using` que falten al principio del archivo:

```csharp
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
```

En `tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs`, ubicar (línea ~586-589):

```csharp
        var tareaServiceMock = new Mock<ITareaService>();
        var confirmMock = new Mock<IConfirmacionService>();
        var formVm = new TareaFormViewModel(tareaServiceMock.Object, sessionMock.Object, navMock.Object, confirmMock.Object);
```

y reemplazarlo por:

```csharp
        var tareaServiceMock = new Mock<ITareaService>();
        var confirmMock = new Mock<IConfirmacionService>();
        var panelClasificacion = new ClasificacionTareaPanelViewModel(
            Mock.Of<IZonaService>(), Mock.Of<IDimensionTematicaService>(),
            Mock.Of<IOrganismoResponsableService>(), Mock.Of<IOrigenFinanciamientoService>(),
            Mock.Of<IDocumentoAdministrativoService>());
        var formVm = new TareaFormViewModel(
            tareaServiceMock.Object, sessionMock.Object, navMock.Object, confirmMock.Object,
            panelClasificacion, Mock.Of<IClasificacionTareaDialogService>());
```

Agregar `using StockApp.Application.Catalogo;` y `using StockApp.Application.Documentos;` al principio de ese archivo si no están.

En `tests/StockApp.Presentation.UiTests/TareaFormViewTests.cs`, reemplazar los dos helpers `MontarParaCrear`/`MontarParaVer`:

```csharp
    private static (Window Window, TareaFormViewModel Vm, TareaServiceFake Servicio, NavigationRecorderFake Nav) MontarParaCrear(
        RolUsuario rol = RolUsuario.Admin)
    {
        var servicio = new TareaServiceFake();
        var nav = new NavigationRecorderFake();
        var vm = new TareaFormViewModel(servicio, new SesionFake(rol), nav, new ConfirmacionServiceFake());
        vm.CargarParaCrear();

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja asentar bindings de Command/IsEnabled

        return (window, vm, servicio, nav);
    }

    private static (Window Window, TareaFormViewModel Vm, TareaServiceFake Servicio, NavigationRecorderFake Nav) MontarParaVer(
        Tarea tarea, RolUsuario rol = RolUsuario.Admin, TareaServiceFake? servicioExistente = null)
    {
        var servicio = servicioExistente ?? new TareaServiceFake(new System.Collections.Generic.List<Tarea> { tarea });
        var nav = new NavigationRecorderFake();
        var vm = new TareaFormViewModel(servicio, new SesionFake(rol), nav, new ConfirmacionServiceFake());
        vm.CargarParaVer(tarea);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja asentar bindings de Command/IsEnabled

        return (window, vm, servicio, nav);
    }
```

por:

```csharp
    private static ClasificacionTareaPanelViewModel NuevoPanelClasificacion() => new(
        new ZonaServiceFake(), new DimensionTematicaServiceFake(),
        new OrganismoResponsableServiceFake(), new OrigenFinanciamientoServiceFake(),
        new DocumentoServiceFake());

    private static (Window Window, TareaFormViewModel Vm, TareaServiceFake Servicio, NavigationRecorderFake Nav,
                     ClasificacionDialogServiceFake DialogoClasificacion) MontarParaCrear(
        RolUsuario rol = RolUsuario.Admin)
    {
        var servicio = new TareaServiceFake();
        var nav = new NavigationRecorderFake();
        var dialogoClasificacion = new ClasificacionDialogServiceFake();
        var vm = new TareaFormViewModel(
            servicio, new SesionFake(rol), nav, new ConfirmacionServiceFake(),
            NuevoPanelClasificacion(), dialogoClasificacion);
        vm.CargarParaCrear();

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja asentar bindings de Command/IsEnabled

        return (window, vm, servicio, nav, dialogoClasificacion);
    }

    private static (Window Window, TareaFormViewModel Vm, TareaServiceFake Servicio, NavigationRecorderFake Nav,
                     ClasificacionDialogServiceFake DialogoClasificacion) MontarParaVer(
        Tarea tarea, RolUsuario rol = RolUsuario.Admin, TareaServiceFake? servicioExistente = null)
    {
        var servicio = servicioExistente ?? new TareaServiceFake(new System.Collections.Generic.List<Tarea> { tarea });
        var nav = new NavigationRecorderFake();
        var dialogoClasificacion = new ClasificacionDialogServiceFake();
        var vm = new TareaFormViewModel(
            servicio, new SesionFake(rol), nav, new ConfirmacionServiceFake(),
            NuevoPanelClasificacion(), dialogoClasificacion);
        vm.CargarParaVer(tarea);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja asentar bindings de Command/IsEnabled

        return (window, vm, servicio, nav, dialogoClasificacion);
    }
```

Los llamadores existentes en ese archivo desestructuran la tupla como `var (window, vm, servicio, nav) = MontarParaCrear();` / `MontarParaVer(...)` — actualizar CADA llamador para agregar el quinto elemento `dialogoClasificacion` a la desestructuración (ej. `var (window, vm, servicio, nav, _) = MontarParaCrear();` en los que no lo necesiten, sin `_` en los que sí — ver Task 14 para el caso que sí lo necesita).

En `tests/StockApp.Presentation.UiTests/InicioPanelTareasTests.cs`, ubicar las dos ocurrencias de:

```csharp
        var formVm = new TareaFormViewModel(
            new TareaServiceFake(), new SesionFake(RolUsuario.Admin), navegacion, new ConfirmacionServiceFake());
```

y reemplazar CADA una por:

```csharp
        var formVm = new TareaFormViewModel(
            new TareaServiceFake(), new SesionFake(RolUsuario.Admin), navegacion, new ConfirmacionServiceFake(),
            new ClasificacionTareaPanelViewModel(
                new ZonaServiceFake(), new DimensionTematicaServiceFake(),
                new OrganismoResponsableServiceFake(), new OrigenFinanciamientoServiceFake(),
                new DocumentoServiceFake()),
            new ClasificacionDialogServiceFake());
```

Agregar al final de la clase `TareaFormViewModelTests` en `tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFormViewModelTests.cs` (antes del último `}`) — estos tests cubren el comportamiento NUEVO de `TareaFormViewModel` a nivel VM (el guardián de la VISTA real para `MuestraReclasificar` va en Task 14; acá se cubre la lógica del booleano y el flujo del comando):

```csharp

    // ── Clasificación en el alta (spec 2026-09-08) ──────────────────────────────

    [Fact]
    public async Task GuardarAsync_ConClasificacionElegida_LaIncluyeEnLaTareaCreada()
    {
        var ctx = Crear();
        ctx.Vm.CargarParaCrear();
        ctx.Vm.Titulo = "Reparar bache";
        ctx.Vm.ClasificacionPanel.ZonaSeleccionada = new Zona { Id = 3, Nombre = "Centro" };

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.CrearAsync(It.Is<Tarea>(t => t.ZonaId == 3)), Times.Once);
    }

    // ── MuestraReclasificar (D9 del spec): NO copia MuestraCambioPrioridad ──────

    [Fact]
    public void MuestraReclasificar_AdminConTareaTerminada_EsTrue()
    {
        // El caso que distingue esta fórmula de MuestraCambioPrioridad (D9): la
        // reclasificación alcanza también tareas terminales.
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Vm.CargarParaVer(new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Terminada });

        Assert.True(ctx.Vm.MuestraReclasificar);
    }

    [Fact]
    public void MuestraReclasificar_OperadorConTareaTerminada_EsFalse()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        ctx.Vm.CargarParaVer(new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Terminada });

        Assert.False(ctx.Vm.MuestraReclasificar);
    }

    [Fact]
    public void MuestraReclasificar_AdminEnModoAlta_EsFalse()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Vm.CargarParaCrear();

        Assert.False(ctx.Vm.MuestraReclasificar);
    }

    // ── ReclasificarAsync (comando) ──────────────────────────────────────────────

    [Fact]
    public async Task ReclasificarCommand_ModalCancelado_NoLlamaAlServicio()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        ctx.Vm.CargarParaVer(new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente });
        ctx.DialogoClasificacion.Setup(d => d.PedirClasificacionAsync(It.IsAny<DatosClasificacionTarea>()))
            .ReturnsAsync((DatosClasificacionTarea?)null);

        await ctx.Vm.ReclasificarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ReclasificarAsync(It.IsAny<int>(), It.IsAny<DatosClasificacionTarea>()), Times.Never);
    }

    [Fact]
    public async Task ReclasificarCommand_ModalConfirmado_LlamaAlServicioYRefrescaLaTarea()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Vm.CargarParaVer(tarea);

        var nuevaClasificacion = new DatosClasificacionTarea(3, null, null, null, null);
        ctx.DialogoClasificacion.Setup(d => d.PedirClasificacionAsync(It.IsAny<DatosClasificacionTarea>()))
            .ReturnsAsync(nuevaClasificacion);
        ctx.Svc.Setup(s => s.ReclasificarAsync(1, nuevaClasificacion)).Returns(Task.CompletedTask);
        var tareaActualizada = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente,
            ZonaId = 3, Zona = new Zona { Id = 3, Nombre = "Centro" },
        };
        ctx.Svc.Setup(s => s.ObtenerPorIdAsync(1)).ReturnsAsync(tareaActualizada);

        await ctx.Vm.ReclasificarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.ReclasificarAsync(1, nuevaClasificacion), Times.Once);
        Assert.Equal("Centro", ctx.Vm.ZonaTexto);
    }
```

Los `using` del archivo (`StockApp.Domain.Entities` por `Zona`/`Tarea`) ya están presentes — no hace falta ningún `using` nuevo para estos tests.

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~TareaFormViewModelTests"`
Expected: FAIL con error de compilación — el constructor de `TareaFormViewModel` no acepta 6 argumentos.

- [ ] **Step 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Tareas/TareaFormViewModel.cs`, reemplazar el bloque de campos/propiedades/constructor:

```csharp
    private readonly ITareaService        _service;
    private readonly ICurrentSession      _session;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    private int _idTarea;

    /// <summary>
    /// Espejo de Tarea.EsTerminal para la tarea cargada (decisión 14 del spec): se consulta
    /// una sola vez en CargarParaVer, no se recalcula con un estado propio del VM, para no
    /// duplicar el conocimiento de qué estados son terminales fuera del dominio.
    /// </summary>
    private bool _tareaEsTerminal;

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

    /// <summary>
    /// Decisión 14 del spec: además de ser Admin y no estar en modo alta, la tarea no puede
    /// estar en un estado terminal — un botón que siempre va a fallar con 409 es peor que no
    /// tener botón.
    /// </summary>
    public bool MuestraCambioPrioridad => EsAdmin && !EsNuevaTarea && !_tareaEsTerminal;

    public TareaFormViewModel(
        ITareaService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }
```

por:

```csharp
    private readonly ITareaService        _service;
    private readonly ICurrentSession      _session;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;
    private readonly IClasificacionTareaDialogService _dialogoClasificacion;

    private int _idTarea;

    /// <summary>
    /// Espejo de Tarea.EsTerminal para la tarea cargada (decisión 14 del spec): se consulta
    /// una sola vez en CargarParaVer, no se recalcula con un estado propio del VM, para no
    /// duplicar el conocimiento de qué estados son terminales fuera del dominio.
    /// </summary>
    private bool _tareaEsTerminal;

    /// <summary>Ids actuales de clasificación de la tarea cargada (spec 2026-09-08): precarga
    /// el modal de reclasificación (Task 10) — D21, el Admin ve exactamente lo que hay
    /// guardado. Se recalcula en CargarParaVer, no en el alta (ahí no hay "actual").</summary>
    private DatosClasificacionTarea _clasificacionActual = new(null, null, null, null, null);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _titulo = string.Empty;

    [ObservableProperty] private string? _descripcion;
    [ObservableProperty] private DateTime? _fechaLimiteSeleccionada;
    [ObservableProperty] private string _estadoTexto = string.Empty;
    [ObservableProperty] private string? _tomadaPorNombre;
    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty] private string? _zonaTexto;
    [ObservableProperty] private string? _dimensionTematicaTexto;
    [ObservableProperty] private string? _organismoResponsableTexto;
    [ObservableProperty] private string? _origenFinanciamientoTexto;
    [ObservableProperty] private string? _documentoAdministrativoTexto;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuestraCambioPrioridad))]
    [NotifyPropertyChangedFor(nameof(MuestraReclasificar))]
    private bool _esNuevaTarea = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AgregarNotaCommand))]
    private string _nuevaNotaTexto = string.Empty;

    [ObservableProperty] private PrioridadTarea _prioridadSeleccionada;

    public ObservableCollection<NotaTarea> Notas { get; } = new();
    public IReadOnlyList<PrioridadTarea> PrioridadesDisponibles { get; } =
        new[] { PrioridadTarea.Baja, PrioridadTarea.Media, PrioridadTarea.Alta };

    /// <summary>Panel embebido de clasificación (Task 9): solo se usa en modo alta — en
    /// modo detalle los cinco campos se muestran de solo lectura (ZonaTexto, etc.) y se
    /// reclasifican vía el modal (ReclasificarCommand), no editando este panel in-place.</summary>
    public ClasificacionTareaPanelViewModel ClasificacionPanel { get; }

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    /// <summary>
    /// Decisión 14 del spec: además de ser Admin y no estar en modo alta, la tarea no puede
    /// estar en un estado terminal — un botón que siempre va a fallar con 409 es peor que no
    /// tener botón.
    /// </summary>
    public bool MuestraCambioPrioridad => EsAdmin && !EsNuevaTarea && !_tareaEsTerminal;

    /// <summary>
    /// Botón "Reclasificar" (spec 2026-09-08, D9): a propósito SIN la condición
    /// !_tareaEsTerminal que sí tiene MuestraCambioPrioridad — la reclasificación alcanza
    /// también tareas Terminada/Cancelada (corrige un reporte mal salido sin reabrir la
    /// tarea). Copiar MuestraCambioPrioridad tal cual acá sería un bug.
    /// </summary>
    public bool MuestraReclasificar => EsAdmin && !EsNuevaTarea;

    public TareaFormViewModel(
        ITareaService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion,
        ClasificacionTareaPanelViewModel clasificacionPanel,
        IClasificacionTareaDialogService dialogoClasificacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
        ClasificacionPanel = clasificacionPanel;
        _dialogoClasificacion = dialogoClasificacion;
    }
```

Reemplazar `CargarParaVer`:

```csharp
    public void CargarParaVer(Tarea tarea)
    {
        _idTarea = tarea.Id;
        _tareaEsTerminal = tarea.EsTerminal;
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
```

por:

```csharp
    public void CargarParaVer(Tarea tarea)
    {
        _idTarea = tarea.Id;
        _tareaEsTerminal = tarea.EsTerminal;
        EsNuevaTarea = false;
        Titulo = tarea.Titulo;
        Descripcion = tarea.Descripcion;
        FechaLimiteSeleccionada = tarea.FechaLimite;
        EstadoTexto = tarea.Estado.ToString();
        TomadaPorNombre = tarea.TomadaPor?.NombreUsuario;
        PrioridadSeleccionada = tarea.Prioridad;
        MensajeError = null;

        ZonaTexto = tarea.Zona?.Nombre;
        DimensionTematicaTexto = tarea.DimensionTematica?.Nombre;
        OrganismoResponsableTexto = tarea.OrganismoResponsable?.Nombre;
        OrigenFinanciamientoTexto = tarea.OrigenFinanciamiento?.Nombre;
        DocumentoAdministrativoTexto = tarea.DocumentoAdministrativo is null
            ? null : $"{tarea.DocumentoAdministrativo.Tipo} {tarea.DocumentoAdministrativo.Numero}/{tarea.DocumentoAdministrativo.Anio}";
        _clasificacionActual = new DatosClasificacionTarea(
            tarea.ZonaId, tarea.DimensionTematicaId, tarea.OrganismoResponsableId,
            tarea.OrigenFinanciamientoId, tarea.DocumentoAdministrativoId);

        Notas.Clear();
        foreach (var nota in tarea.Notas)
            Notas.Add(nota);
    }
```

Reemplazar `GuardarAsync`:

```csharp
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
                // Fix (review final, Important): mismo criterio que el resto de los VMs con
                // fecha del proyecto (Gasto/Ingreso Form, IngresoPorFactura, PagosGasto) —
                // si el CalendarDatePicker entrega Kind=Local, Npgsql rechaza el insert en
                // timestamptz (el converter del servidor solo normaliza Unspecified).
                FechaLimite = FechaLimiteSeleccionada.HasValue
                    ? DateTime.SpecifyKind(FechaLimiteSeleccionada.Value.Date, DateTimeKind.Utc)
                    : null,
            });
            _navigation.Navegar<TareaListViewModel>();
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }
```

por:

```csharp
    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            var clasificacion = ClasificacionPanel.ObtenerDatos();
            await _service.CrearAsync(new Tarea
            {
                Titulo = Titulo,
                Descripcion = string.IsNullOrWhiteSpace(Descripcion) ? null : Descripcion,
                // Fix (review final, Important): mismo criterio que el resto de los VMs con
                // fecha del proyecto (Gasto/Ingreso Form, IngresoPorFactura, PagosGasto) —
                // si el CalendarDatePicker entrega Kind=Local, Npgsql rechaza el insert en
                // timestamptz (el converter del servidor solo normaliza Unspecified).
                FechaLimite = FechaLimiteSeleccionada.HasValue
                    ? DateTime.SpecifyKind(FechaLimiteSeleccionada.Value.Date, DateTimeKind.Utc)
                    : null,
                ZonaId = clasificacion.ZonaId,
                DimensionTematicaId = clasificacion.DimensionTematicaId,
                OrganismoResponsableId = clasificacion.OrganismoResponsableId,
                OrigenFinanciamientoId = clasificacion.OrigenFinanciamientoId,
                DocumentoAdministrativoId = clasificacion.DocumentoAdministrativoId,
            });
            _navigation.Navegar<TareaListViewModel>();
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }
```

Agregar el comando nuevo después de `CambiarPrioridadAsync`:

```csharp
    [RelayCommand]
    private async Task ReclasificarAsync()
    {
        MensajeError = null;
        try
        {
            var resultado = await _dialogoClasificacion.PedirClasificacionAsync(_clasificacionActual);
            if (resultado is null) return; // el Admin canceló el modal

            await _service.ReclasificarAsync(_idTarea, resultado);

            // Molde de DocumentoFormViewModel.RecargarAsync: refresca desde el servidor y
            // vuelve a popular los campos de solo lectura vía CargarParaVer, en vez de
            // mantener una copia local optimista.
            var actualizada = await _service.ObtenerPorIdAsync(_idTarea);
            if (actualizada is not null)
                CargarParaVer(actualizada);

            await _confirmacion.InformarAsync("Clasificación actualizada.");
        }
        catch (Exception ex)
        {
            MensajeError = ResolverMensajeError(ex);
        }
    }
```

`IClasificacionTareaDialogService` vive en `StockApp.Presentation.Services`, mismo namespace que `IConfirmacionService`/`ICurrentSession` (ya importado en este archivo) — no hace falta ningún `using` nuevo para este Step.

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~TareaFormViewModelTests|FullyQualifiedName~InicioViewModelTests"`
Expected: PASS (las dos clases).

Run: `dotnet test tests/StockApp.Presentation.UiTests --filter "FullyQualifiedName~TareaFormViewTests|FullyQualifiedName~InicioPanelTareasTests"`
Expected: PASS (las dos clases).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Tareas/TareaFormViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFormViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs tests/StockApp.Presentation.UiTests/TareaFormViewTests.cs tests/StockApp.Presentation.UiTests/InicioPanelTareasTests.cs
git commit -m "feat(tareas): TareaFormViewModel — alta con clasificación y comando Reclasificar"
```

---

### Task 13: `TareaFormView.axaml` — UI de clasificación (alta, detalle, botón Reclasificar) + wiring

**Files:**
- Modify: `src/StockApp.Presentation/Views/Tareas/TareaFormView.axaml`
- Modify: `src/StockApp.Presentation/Views/Tareas/TareaFormView.axaml.cs`

**Interfaces:**
- Consumes: `TareaFormViewModel.ClasificacionPanel`/`ZonaTexto`/etc./`MuestraReclasificar`/`ReclasificarCommand` (Task 12).

Este task es puramente de vista — no tiene test propio (el guardián de comportamiento va en Task 14). Se verifica corriendo la suite de UiTests existente para confirmar que no rompió nada.

- [ ] **Step 1: Extender `TareaFormView.axaml`**

Reemplazar el bloque de alta:

```xml
                    <!-- Alta: título/descripción/fecha límite editables -->
                    <StackPanel Spacing="{DynamicResource Espacio3}" IsVisible="{Binding EsNuevaTarea}">
                        <c:CampoFormulario Etiqueta="Título">
                            <TextBox Text="{Binding Titulo}" Watermark="Ej.: Reparar bache en calle Rivera" />
                        </c:CampoFormulario>

                        <c:CampoFormulario Etiqueta="Descripción (opcional)">
                            <TextBox Text="{Binding Descripcion}" AcceptsReturn="True" Height="80"
                                      Watermark="Detalle del trabajo a realizar" />
                        </c:CampoFormulario>

                        <c:CampoFormulario Etiqueta="Fecha límite (opcional)">
                            <CalendarDatePicker SelectedDate="{Binding FechaLimiteSeleccionada}"
                                                PlaceholderText="dd/mm/aaaa"
                                                SelectedDateFormat="Custom"
                                                CustomDateFormatString="dd/MM/yyyy"
                                                beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                        </c:CampoFormulario>

                        <StackPanel Orientation="Horizontal" Spacing="{DynamicResource Espacio2}">
                            <Button Classes="primary" Content="Guardar" Command="{Binding GuardarCommand}" />
                            <Button Classes="secondary" Content="Volver" Command="{Binding VolverCommand}" />
                        </StackPanel>
                    </StackPanel>
```

por:

```xml
                    <!-- Alta: título/descripción/fecha límite editables -->
                    <StackPanel Spacing="{DynamicResource Espacio3}" IsVisible="{Binding EsNuevaTarea}">
                        <c:CampoFormulario Etiqueta="Título">
                            <TextBox Text="{Binding Titulo}" Watermark="Ej.: Reparar bache en calle Rivera" />
                        </c:CampoFormulario>

                        <c:CampoFormulario Etiqueta="Descripción (opcional)">
                            <TextBox Text="{Binding Descripcion}" AcceptsReturn="True" Height="80"
                                      Watermark="Detalle del trabajo a realizar" />
                        </c:CampoFormulario>

                        <c:CampoFormulario Etiqueta="Fecha límite (opcional)">
                            <CalendarDatePicker SelectedDate="{Binding FechaLimiteSeleccionada}"
                                                PlaceholderText="dd/mm/aaaa"
                                                SelectedDateFormat="Custom"
                                                CustomDateFormatString="dd/MM/yyyy"
                                                beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                        </c:CampoFormulario>

                        <TextBlock Text="Clasificación (opcional)" Classes="seccion" Margin="0,8,0,0" />
                        <pnl:ClasificacionTareaPanelView DataContext="{Binding ClasificacionPanel}" />

                        <StackPanel Orientation="Horizontal" Spacing="{DynamicResource Espacio2}">
                            <Button Classes="primary" Content="Guardar" Command="{Binding GuardarCommand}" />
                            <Button Classes="secondary" Content="Volver" Command="{Binding VolverCommand}" />
                        </StackPanel>
                    </StackPanel>
```

Reemplazar el bloque de detalle:

```xml
                    <!-- Detalle de solo lectura: título/descripción/fecha límite/estado/tomada por -->
                    <StackPanel Spacing="6" IsVisible="{Binding !EsNuevaTarea}">
                        <TextBlock Text="{Binding Titulo}" Classes="seccion" />
                        <TextBlock Text="{Binding Descripcion}" TextWrapping="Wrap" Foreground="{DynamicResource TextoSecundarioBrush}"
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
```

por:

```xml
                    <!-- Detalle de solo lectura: título/descripción/fecha límite/estado/tomada por -->
                    <StackPanel Spacing="6" IsVisible="{Binding !EsNuevaTarea}">
                        <TextBlock Text="{Binding Titulo}" Classes="seccion" />
                        <TextBlock Text="{Binding Descripcion}" TextWrapping="Wrap" Foreground="{DynamicResource TextoSecundarioBrush}"
                                   IsVisible="{Binding Descripcion, Converter={x:Static ObjectConverters.IsNotNull}}" />
                        <TextBlock Text="{Binding FechaLimiteSeleccionada, StringFormat='Vence: {0:dd/MM/yyyy}'}"
                                   Classes="caption"
                                   IsVisible="{Binding FechaLimiteSeleccionada, Converter={x:Static ObjectConverters.IsNotNull}}" />
                        <TextBlock Text="{Binding EstadoTexto, StringFormat='Estado: {0}'}" Classes="caption" />
                        <TextBlock Text="{Binding TomadaPorNombre, StringFormat='Tomada por: {0}'}" Classes="caption"
                                   IsVisible="{Binding TomadaPorNombre, Converter={x:Static ObjectConverters.IsNotNull}}" />

                        <!-- Clasificación de solo lectura (spec 2026-09-08): reclasificar es
                             SIEMPRE vía el modal (Reclasificar más abajo), nunca editando estos
                             campos in-place. -->
                        <TextBlock Text="{Binding ZonaTexto, StringFormat='Zona: {0}'}" Classes="caption"
                                   IsVisible="{Binding ZonaTexto, Converter={x:Static ObjectConverters.IsNotNull}}" />
                        <TextBlock Text="{Binding DimensionTematicaTexto, StringFormat='Dimensión: {0}'}" Classes="caption"
                                   IsVisible="{Binding DimensionTematicaTexto, Converter={x:Static ObjectConverters.IsNotNull}}" />
                        <TextBlock Text="{Binding OrganismoResponsableTexto, StringFormat='Organismo: {0}'}" Classes="caption"
                                   IsVisible="{Binding OrganismoResponsableTexto, Converter={x:Static ObjectConverters.IsNotNull}}" />
                        <TextBlock Text="{Binding OrigenFinanciamientoTexto, StringFormat='Origen de financiamiento: {0}'}" Classes="caption"
                                   IsVisible="{Binding OrigenFinanciamientoTexto, Converter={x:Static ObjectConverters.IsNotNull}}" />
                        <TextBlock Text="{Binding DocumentoAdministrativoTexto, StringFormat='Expediente: {0}'}" Classes="caption"
                                   IsVisible="{Binding DocumentoAdministrativoTexto, Converter={x:Static ObjectConverters.IsNotNull}}" />

                        <Button Classes="secondary" Content="Reclasificar" Command="{Binding ReclasificarCommand}"
                                IsVisible="{Binding MuestraReclasificar}"
                                HorizontalAlignment="Left" Margin="0,8,0,0" />

                        <Button Classes="secondary" Content="Volver" Command="{Binding VolverCommand}"
                                HorizontalAlignment="Left" Margin="0,8,0,0" />
                    </StackPanel>
```

Agregar el xmlns nuevo en la raíz del `UserControl` (junto a los `xmlns:c`/`xmlns:beh` existentes):

```xml
             xmlns:pnl="using:StockApp.Presentation.Views.Tareas"
```

- [ ] **Step 2: Wiring de `DataContextChanged`**

Reemplazar `src/StockApp.Presentation/Views/Tareas/TareaFormView.axaml.cs` completo:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.Views.Tareas;

/// <summary>
/// A partir de la clasificación (spec 2026-09-08), el modo alta SÍ tiene combos que
/// precargar de forma asíncrona (Zona/Dimensión/Organismo/Origen vía ClasificacionPanel),
/// así que este archivo pasa a necesitar el wiring de DataContextChanged que antes no hacía
/// falta (el comentario viejo de esta clase, "sin combos que precargar", ya no es cierto) —
/// mismo criterio que GastoFormView. CargarParaCrear()/CargarParaVer() siguen siendo
/// síncronos y corren ANTES de que la navegación publique el VM como DataContext;
/// DataContextChanged solo dispara la carga ASÍNCRONA del panel de clasificación, y SOLO en
/// modo alta (en modo detalle los cinco campos son de solo lectura, no hace falta poblar
/// combos).
/// </summary>
public partial class TareaFormView : UserControl
{
    public TareaFormView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is TareaFormViewModel vm && vm.EsNuevaTarea)
                await vm.ClasificacionPanel.InicializarAsync();
        };
    }
}
```

- [ ] **Step 3: Correr la suite existente y confirmar que no se rompió nada**

Run: `dotnet test tests/StockApp.Presentation.UiTests --filter "FullyQualifiedName~TareaFormViewTests"`
Expected: PASS (toda la clase — el guardián específico del botón Reclasificar llega en Task 14).

- [ ] **Step 4: Commit**
```bash
git add src/StockApp.Presentation/Views/Tareas/TareaFormView.axaml src/StockApp.Presentation/Views/Tareas/TareaFormView.axaml.cs
git commit -m "feat(tareas): TareaFormView — UI de clasificación en alta y detalle"
```

---

### Task 14: guardián crítico — botón "Reclasificar" visible para Admin en tarea Terminada (D9)

**Files:**
- Modify: `tests/StockApp.Presentation.UiTests/TareaFormViewTests.cs`

**Interfaces:**
- Consumes: `TareaFormView` real (Task 13), `TareaFormViewModel.MuestraReclasificar` (Task 12).

Este es el guardián que exige explícitamente la instrucción 6 del plan: un test de ViewModel NO alcanza para custodiar la visibilidad declarada en el XAML (memoria del repo: "un guard sobre comportamiento que ya anda no puede fallar primero" — acá se verifica por mutación en el Step 2, no se razona).

- [ ] **Step 1: Escribir el test que falla**

Agregar al final de la clase `TareaFormViewTests` en `tests/StockApp.Presentation.UiTests/TareaFormViewTests.cs` (antes del último `}`):

```csharp

    // ── Reclasificar (spec 2026-09-08, D9): guardián con la View real ──────────

    [AvaloniaFact]
    public void ModoDetalle_AdminConTareaTerminada_MuestraBotonReclasificar()
    {
        // EL guardián crítico de D9: MuestraReclasificar es EsAdmin && !EsNuevaTarea, A
        // PROPÓSITO sin la condición !_tareaEsTerminal que sí tiene MuestraCambioPrioridad.
        // Copiar esa fórmula tal cual acá sería un bug — este test lo agarra en el XAML real,
        // no solo a nivel VM.
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Terminada };
        var (window, vm, _, _, _) = MontarParaVer(tarea, rol: RolUsuario.Admin);

        Assert.True(vm.MuestraReclasificar);
        Assert.True(BotonVisiblePorTexto(window, "Reclasificar").IsVisible);
    }

    [AvaloniaFact]
    public void ModoDetalle_OperadorConTareaTerminada_NoMuestraBotonReclasificar()
    {
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Terminada };
        var (window, vm, _, _, _) = MontarParaVer(tarea, rol: RolUsuario.Operador);

        Assert.False(vm.MuestraReclasificar);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
            b => Equals(b.Content, "Reclasificar") && ArbolVisual.EsVisibleEnArbol(b));
    }

    [AvaloniaFact]
    public void ModoDetalle_AdminConTareaPendiente_MuestraBotonReclasificar()
    {
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente };
        var (window, vm, _, _, _) = MontarParaVer(tarea, rol: RolUsuario.Admin);

        Assert.True(vm.MuestraReclasificar);
        Assert.True(BotonVisiblePorTexto(window, "Reclasificar").IsVisible);
    }

    [AvaloniaFact]
    public void ModoAlta_Admin_NoMuestraBotonReclasificar()
    {
        var (window, vm, _, _, _) = MontarParaCrear(rol: RolUsuario.Admin);

        Assert.False(vm.MuestraReclasificar);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
            b => Equals(b.Content, "Reclasificar") && ArbolVisual.EsVisibleEnArbol(b));
    }

    [AvaloniaFact]
    public void ClickReal_EnReclasificar_LlamaAlDialogoYAlServicioConElResultadoYRefrescaLosTextos()
    {
        var tarea = new Tarea
        {
            Id = 5, Titulo = "x", Estado = EstadoTarea.Terminada,
            ZonaId = 1, Zona = new Zona { Id = 1, Nombre = "Centro" },
        };
        var servicio = new TareaServiceFake(new System.Collections.Generic.List<Tarea> { tarea });
        var (window, vm, _, _, dialogoClasificacion) = MontarParaVer(tarea, rol: RolUsuario.Admin, servicioExistente: servicio);
        dialogoClasificacion.ResultadoADevolver = new DatosClasificacionTarea(null, null, null, null, null);

        var boton = BotonVisiblePorTexto(window, "Reclasificar");
        Clickear(window, boton);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, dialogoClasificacion.Llamadas);
        Assert.Equal(1, dialogoClasificacion.UltimoActualRecibido?.ZonaId);
        Assert.Contains((5, new DatosClasificacionTarea(null, null, null, null, null)), servicio.Reclasificaciones);
        Assert.Null(vm.ZonaTexto);
    }
```

Actualizar TODOS los llamadores preexistentes de `MontarParaCrear()`/`MontarParaVer(...)` en este mismo archivo (los que ya estaban antes de este plan) para desestructurar el quinto elemento de la tupla con `_` — ej. `var (window, vm, _, _) = MontarParaCrear();` pasa a `var (window, vm, _, _, _) = MontarParaCrear();`. Repetir en cada uno de los ~9 sitios existentes del archivo.

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.UiTests --filter "FullyQualifiedName~TareaFormViewTests.ModoDetalle_AdminConTareaTerminada_MuestraBotonReclasificar"`
Expected: FAIL — `BotonVisiblePorTexto` no encuentra ningún botón con contenido "Reclasificar" visible (`InvalidOperationException: Sequence contains no matching element`), porque `MuestraReclasificar` en el XAML real solo se agregó como binding — confirmar que efectivamente falla ANTES de dar el Step 3 por bueno.

- [ ] **Step 3: Verificación por mutación (obligatoria antes de dar la task por cerrada)**

Con la implementación de Task 12/13 ya en el árbol, correr la suite completa una vez (debe estar en verde). Después, mutar deliberadamente `TareaFormViewModel.MuestraReclasificar` para que copie la fórmula de `MuestraCambioPrioridad` (`EsAdmin && !EsNuevaTarea && !_tareaEsTerminal`) y volver a correr `ModoDetalle_AdminConTareaTerminada_MuestraBotonReclasificar`: tiene que fallar. Revertir la mutación con `git checkout -- src/StockApp.Presentation/ViewModels/Tareas/TareaFormViewModel.cs` y confirmar que la suite vuelve a verde.

Run: `dotnet test tests/StockApp.Presentation.UiTests --filter "FullyQualifiedName~TareaFormViewTests"`
Expected: PASS (con la implementación real, sin la mutación).

- [ ] **Step 4: Commit**
```bash
git add tests/StockApp.Presentation.UiTests/TareaFormViewTests.cs
git commit -m "test(tareas): guardián D9 — Reclasificar visible para Admin en tarea Terminada"
```

---

### Task 15: `TareaFila.EtiquetasClasificacion` + tarjetas de `TareaListView` (D19)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Tareas/TareaListViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/Tareas/TareaListView.axaml`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFilaTests.cs`

**Interfaces:**
- Consumes: `Tarea.Zona`/`DimensionTematica`/`OrganismoResponsable`/`OrigenFinanciamiento` (Task 1).
- Produces: `TareaFila.EtiquetasClasificacion` (string?) — consumido por las tarjetas de Pendientes/En curso (D19: `TareaListView` sigue siendo `ItemsControl` con tarjetas, NO se migra a grilla; los clasificadores se muestran como etiquetas dentro de la tarjeta).

Decisión de este task: la etiqueta se agrega a las tarjetas de **Pendientes** y **En curso** (las dos plantillas ricas, con Prioridad/FechaLimite/TomadaPorNombre) — Terminadas/Canceladas son tarjetas minimalistas por diseño (solo título) y agregar cinco clasificadores ahí las saturaría sin aportar nada que el operador vaya a accionar sobre una tarea cerrada desde la lista.

- [ ] **Step 1: Escribir el test que falla**

Agregar al final de la clase `TareaFilaTests` en `tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFilaTests.cs` (antes del último `}`):

```csharp

    // ── EtiquetasClasificacion (spec 2026-09-08, D19) ───────────────────────────

    [Fact]
    public void EtiquetasClasificacion_SinNingunClasificador_DevuelveNull()
    {
        var tarea = TareaCon(fechaLimite: null);
        var fila = new TareaFila(tarea, RolUsuario.Operador);

        Assert.Null(fila.EtiquetasClasificacion);
    }

    [Fact]
    public void EtiquetasClasificacion_ConAlgunosClasificadores_LosUneConPuntoMedio()
    {
        var tarea = TareaCon(fechaLimite: null);
        tarea.Zona = new Zona { Id = 1, Nombre = "Centro" };
        tarea.OrigenFinanciamiento = new OrigenFinanciamiento { Id = 2, Nombre = "Presupuesto propio" };
        var fila = new TareaFila(tarea, RolUsuario.Operador);

        Assert.Equal("Centro · Presupuesto propio", fila.EtiquetasClasificacion);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~TareaFilaTests.EtiquetasClasificacion"`
Expected: FAIL con error de compilación — `TareaFila` no tiene `EtiquetasClasificacion`.

- [ ] **Step 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Tareas/TareaListViewModel.cs`, ubicar la propiedad `TomadaPorNombre` dentro de `TareaFila`:

```csharp
    public string? TomadaPorNombre => Tarea.TomadaPor?.NombreUsuario;
```

y agregar inmediatamente después:

```csharp
    public string? TomadaPorNombre => Tarea.TomadaPor?.NombreUsuario;

    /// <summary>Etiquetas de clasificación para la tarjeta (spec 2026-09-08, D19): une los
    /// clasificadores asignados con " · ", null si no hay ninguno (así el XAML puede usar
    /// IsNotNull para ocultar la línea entera en vez de mostrar una cadena vacía).</summary>
    public string? EtiquetasClasificacion
    {
        get
        {
            var partes = new List<string>();
            if (Tarea.Zona is not null) partes.Add(Tarea.Zona.Nombre);
            if (Tarea.DimensionTematica is not null) partes.Add(Tarea.DimensionTematica.Nombre);
            if (Tarea.OrganismoResponsable is not null) partes.Add(Tarea.OrganismoResponsable.Nombre);
            if (Tarea.OrigenFinanciamiento is not null) partes.Add(Tarea.OrigenFinanciamiento.Nombre);
            return partes.Count == 0 ? null : string.Join(" · ", partes);
        }
    }
```

Agregar `using System.Collections.Generic;` al principio del archivo si no está ya (`List<T>`).

En `src/StockApp.Presentation/Views/Tareas/TareaListView.axaml`, ubicar (dentro de la tarjeta de **Pendientes**):

```xml
                                            <TextBlock Text="{Binding PrioridadTexto}" Classes="caption" />
                                            <TextBlock Text="{Binding FechaLimite, StringFormat='Vence: {0:dd/MM/yyyy}'}"
                                                       Classes="caption"
                                                       IsVisible="{Binding FechaLimite, Converter={x:Static ObjectConverters.IsNotNull}}" />
                                        </StackPanel>
                                        <Button Grid.Column="1" Classes="ghost" Content="Ver"
                                                Command="{Binding $parent[UserControl].((vm:TareaListViewModel)DataContext).VerDetalleCommand}"
                                                CommandParameter="{Binding}" />
                                        <Button Grid.Column="2" Classes="secondary" Content="Tomar"
```

y reemplazarlo por (agrega el `TextBlock` de etiquetas antes del cierre del `StackPanel` de la columna 0):

```xml
                                            <TextBlock Text="{Binding PrioridadTexto}" Classes="caption" />
                                            <TextBlock Text="{Binding FechaLimite, StringFormat='Vence: {0:dd/MM/yyyy}'}"
                                                       Classes="caption"
                                                       IsVisible="{Binding FechaLimite, Converter={x:Static ObjectConverters.IsNotNull}}" />
                                            <TextBlock Text="{Binding EtiquetasClasificacion}"
                                                       Classes="caption" Foreground="{DynamicResource TextoTerciarioBrush}"
                                                       IsVisible="{Binding EtiquetasClasificacion, Converter={x:Static ObjectConverters.IsNotNull}}" />
                                        </StackPanel>
                                        <Button Grid.Column="1" Classes="ghost" Content="Ver"
                                                Command="{Binding $parent[UserControl].((vm:TareaListViewModel)DataContext).VerDetalleCommand}"
                                                CommandParameter="{Binding}" />
                                        <Button Grid.Column="2" Classes="secondary" Content="Tomar"
```

Y en la tarjeta de **En curso**, ubicar:

```xml
                                            <TextBlock Text="{Binding TomadaPorNombre, StringFormat='Tomada por {0}'}"
                                                       Classes="caption" />
                                        </StackPanel>
                                        <Button Grid.Column="1" Classes="ghost" Content="Ver"
```

y reemplazarlo por:

```xml
                                            <TextBlock Text="{Binding TomadaPorNombre, StringFormat='Tomada por {0}'}"
                                                       Classes="caption" />
                                            <TextBlock Text="{Binding EtiquetasClasificacion}"
                                                       Classes="caption" Foreground="{DynamicResource TextoTerciarioBrush}"
                                                       IsVisible="{Binding EtiquetasClasificacion, Converter={x:Static ObjectConverters.IsNotNull}}" />
                                        </StackPanel>
                                        <Button Grid.Column="1" Classes="ghost" Content="Ver"
```

(esta segunda ocurrencia de `TomadaPorNombre` en el archivo es única — la tarjeta de Pendientes no tiene ese `TextBlock`, así que no hay ambigüedad al buscar el bloque a reemplazar).

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~TareaFilaTests"`
Expected: PASS (toda la clase).

Run: `dotnet test tests/StockApp.Presentation.UiTests --filter "FullyQualifiedName~TareaListViewTests"`
Expected: PASS (la suite existente de la lista sigue verde — el XAML nuevo no rompe ningún binding).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Tareas/TareaListViewModel.cs src/StockApp.Presentation/Views/Tareas/TareaListView.axaml tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFilaTests.cs
git commit -m "feat(tareas): etiquetas de clasificación en las tarjetas de la lista"
```

---

### Task 16: `DocumentoFormViewModel`/`View` — sección "Tareas vinculadas" (D11/D13)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Documentos/DocumentoFormViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/Documentos/DocumentoFormView.axaml`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoFormViewModelTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/DocumentoFormViewGatesTests.cs`

**Interfaces:**
- Consumes: `ITareaService.ListarPorDocumentoAsync` (Task 5).
- Produces: `DocumentoFormViewModel.TareasVinculadas`/`PuedeVerTareasVinculadas` — constructor extendido (1 param nuevo, `ITareaService`), rompe los 2 sitios de construcción existentes (`DocumentoFormViewModelTests.Crear`, `DocumentoFormViewGatesTests.CrearVm`), ambos actualizados en este task.

Sigue el patrón YA resuelto en `InicioViewModel.cs:249-278` (gate `PuedeVerTareas` + `try/catch` propio, panel "Tareas que requieren atención") — mismo gate, mismo criterio de `try/catch` que no debe romper la ficha del documento si `/tareas` falla.

- [ ] **Step 1: Escribir el test que falla**

Reemplazar el helper `Crear` en `tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoFormViewModelTests.cs`:

```csharp
    private static (DocumentoFormViewModel Vm, Mock<IDocumentoAdministrativoService> Svc, Mock<IConfirmacionService> Confirm)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var svc = new Mock<IDocumentoAdministrativoService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Motivo de prueba");
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var adjuntosPanel = new AdjuntosDocumentoPanelViewModel(
            Mock.Of<IAdjuntoDocumentoService>(), Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), confirm.Object, session.Object);

        var vm = new DocumentoFormViewModel(svc.Object, session.Object, nav.Object, confirm.Object, adjuntosPanel);
        return (vm, svc, confirm);
    }
```

por:

```csharp
    private static (DocumentoFormViewModel Vm, Mock<IDocumentoAdministrativoService> Svc, Mock<IConfirmacionService> Confirm,
                     Mock<ITareaService> Tareas)
        Crear(RolUsuario rol = RolUsuario.Admin, IReadOnlySet<string>? permisos = null)
    {
        var svc = new Mock<IDocumentoAdministrativoService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.PermisosActuales).Returns(permisos ?? new HashSet<string>());
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PedirTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync("Motivo de prueba");
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var tareas = new Mock<ITareaService>();

        var adjuntosPanel = new AdjuntosDocumentoPanelViewModel(
            Mock.Of<IAdjuntoDocumentoService>(), Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), confirm.Object, session.Object);

        var vm = new DocumentoFormViewModel(svc.Object, session.Object, nav.Object, confirm.Object, adjuntosPanel, tareas.Object);
        return (vm, svc, confirm, tareas);
    }
```

Agregar `using StockApp.Application.Tareas;` al principio del archivo.

Agregar al final de la clase `DocumentoFormViewModelTests` (antes del último `}`):

```csharp

    // ── Tareas vinculadas (spec 2026-09-08, D11/D13) ────────────────────────────

    [Fact]
    public async Task CargarParaVerAsync_ConPermisoGestionarTareas_CargaLasTareasVinculadas()
    {
        var ctx = Crear(rol: RolUsuario.Operador, permisos: new HashSet<string> { Permisos.GestionarTareas });
        var documento = DocumentoDe(1, EstadoDocumento.Pendiente);
        ctx.Tareas.Setup(t => t.ListarPorDocumentoAsync(1))
            .ReturnsAsync(new List<Tarea> { new() { Id = 9, Titulo = "Vinculada", DocumentoAdministrativoId = 1 } });

        await ctx.Vm.CargarParaVerAsync(documento);

        Assert.True(ctx.Vm.PuedeVerTareasVinculadas);
        Assert.Single(ctx.Vm.TareasVinculadas);
    }

    [Fact]
    public async Task CargarParaVerAsync_SinPermisoGestionarTareas_NoConsultaYQuedaVacio()
    {
        var ctx = Crear(rol: RolUsuario.Operador, permisos: new HashSet<string>());
        var documento = DocumentoDe(1, EstadoDocumento.Pendiente);

        await ctx.Vm.CargarParaVerAsync(documento);

        Assert.False(ctx.Vm.PuedeVerTareasVinculadas);
        Assert.Empty(ctx.Vm.TareasVinculadas);
        ctx.Tareas.Verify(t => t.ListarPorDocumentoAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CargarParaVerAsync_LaLlamadaATareasFalla_NoRompeLaFichaDelDocumento()
    {
        // Mismo criterio que InicioViewModel.cs:249-278 (panel de vencimientos): un fallo
        // consultando /tareas no debe afectar al resto de la ficha del documento.
        var ctx = Crear(rol: RolUsuario.Admin);
        var documento = DocumentoDe(1, EstadoDocumento.Pendiente);
        ctx.Tareas.Setup(t => t.ListarPorDocumentoAsync(1)).ThrowsAsync(new InvalidOperationException("caída"));

        await ctx.Vm.CargarParaVerAsync(documento);

        Assert.Empty(ctx.Vm.TareasVinculadas);
        Assert.Equal(EstadoDocumento.Pendiente.ToString(), ctx.Vm.EstadoTexto); // el resto de la carga sí corrió
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~DocumentoFormViewModelTests"`
Expected: FAIL con error de compilación — el constructor de `DocumentoFormViewModel` no acepta 6 argumentos.

- [ ] **Step 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/Documentos/DocumentoFormViewModel.cs`, reemplazar el bloque de campos + constructor:

```csharp
    private readonly IDocumentoAdministrativoService _service;
    private readonly ICurrentSession                 _session;
    private readonly INavigationService               _navigation;
    private readonly IConfirmacionService              _confirmacion;

    private DocumentoAdministrativo? _documento;
```

por:

```csharp
    private readonly IDocumentoAdministrativoService _service;
    private readonly ICurrentSession                 _session;
    private readonly INavigationService               _navigation;
    private readonly IConfirmacionService              _confirmacion;
    private readonly ITareaService                    _tareas;

    private DocumentoAdministrativo? _documento;
```

Reemplazar el constructor:

```csharp
    public DocumentoFormViewModel(
        IDocumentoAdministrativoService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion,
        AdjuntosDocumentoPanelViewModel adjuntosPanel)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
        AdjuntosPanel = adjuntosPanel;
    }
```

por:

```csharp
    public DocumentoFormViewModel(
        IDocumentoAdministrativoService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion,
        AdjuntosDocumentoPanelViewModel adjuntosPanel, ITareaService tareas)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
        AdjuntosPanel = adjuntosPanel;
        _tareas       = tareas;
    }
```

Agregar, después de `public AdjuntosDocumentoPanelViewModel AdjuntosPanel { get; }`:

```csharp

    public ObservableCollection<Tarea> TareasVinculadas { get; } = new();

    /// <summary>Gate de la sección "Tareas vinculadas" (spec 2026-09-08, D13): un operador
    /// puede tener documentos.gestionar sin tener tareas.gestionar — mismo criterio EXACTO
    /// que InicioViewModel.PuedeVerTareas (InicioViewModel.cs:116-117).</summary>
    public bool PuedeVerTareasVinculadas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarTareas);
```

Reemplazar `CargarParaVerAsync`:

```csharp
    public async Task CargarParaVerAsync(DocumentoAdministrativo documento)
    {
        _documento = documento;
        EsNuevoDocumento = false;
        CargarCamposDesdeDocumento(documento);
        MensajeError = null;

        await AdjuntosPanel.InicializarAsync(documento.Id, documento.EsActivo);
    }
```

por:

```csharp
    public async Task CargarParaVerAsync(DocumentoAdministrativo documento)
    {
        _documento = documento;
        EsNuevoDocumento = false;
        CargarCamposDesdeDocumento(documento);
        MensajeError = null;

        await AdjuntosPanel.InicializarAsync(documento.Id, documento.EsActivo);
        await CargarTareasVinculadasAsync(documento.Id);
    }

    /// <summary>Sección "Tareas vinculadas" (D11/D13): gateada por PuedeVerTareasVinculadas
    /// y con try/catch propio, mismo criterio EXACTO que el panel de vencimientos de
    /// InicioViewModel (InicioViewModel.cs:249-278) — un fallo consultando /tareas no debe
    /// afectar al resto de la ficha del documento.</summary>
    private async Task CargarTareasVinculadasAsync(int documentoId)
    {
        TareasVinculadas.Clear();
        if (!PuedeVerTareasVinculadas) return;

        try
        {
            var tareas = await _tareas.ListarPorDocumentoAsync(documentoId);
            foreach (var tarea in tareas)
                TareasVinculadas.Add(tarea);
        }
        catch (Exception)
        {
            TareasVinculadas.Clear();
        }
    }
```

Agregar los `using` que falten al principio del archivo:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Tareas;
```

En `tests/StockApp.Presentation.UiTests/DocumentoFormViewGatesTests.cs`, reemplazar `CrearVm`:

```csharp
    private static DocumentoFormViewModel CrearVm(RolUsuario rol)
    {
        var sesion = new SesionFake(rol);
        var adjuntosPanel = new AdjuntosDocumentoPanelViewModel(
            new AdjuntoDocumentoServiceFake(),
            new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(),
            new ConfirmacionServiceFake(),
            sesion);

        return new DocumentoFormViewModel(
            new DocumentoServiceFake(), sesion, new NavigationRecorderDocumentosFake(),
            new ConfirmacionServiceFake(), adjuntosPanel);
    }
```

por:

```csharp
    private static DocumentoFormViewModel CrearVm(RolUsuario rol)
    {
        var sesion = new SesionFake(rol);
        var adjuntosPanel = new AdjuntosDocumentoPanelViewModel(
            new AdjuntoDocumentoServiceFake(),
            new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(),
            new ConfirmacionServiceFake(),
            sesion);

        return new DocumentoFormViewModel(
            new DocumentoServiceFake(), sesion, new NavigationRecorderDocumentosFake(),
            new ConfirmacionServiceFake(), adjuntosPanel, new TareaServiceFake());
    }
```

Agregar la UI del bloque nuevo en `src/StockApp.Presentation/Views/Documentos/DocumentoFormView.axaml`, ubicando el `Panel` de Adjuntos:

```xml
                    <Panel IsVisible="{Binding !EsNuevoDocumento}" Margin="0,8,0,0">
                        <adj:AdjuntosDocumentoPanelView DataContext="{Binding AdjuntosPanel}" />
                    </Panel>
```

y agregando inmediatamente después:

```xml
                    <Panel IsVisible="{Binding !EsNuevoDocumento}" Margin="0,8,0,0">
                        <adj:AdjuntosDocumentoPanelView DataContext="{Binding AdjuntosPanel}" />
                    </Panel>

                    <!-- Tareas vinculadas (spec 2026-09-08, D11/D13): gateada por
                         PuedeVerTareasVinculadas (tareas.gestionar) -- un operador puede tener
                         documentos.gestionar sin tener tareas.gestionar. -->
                    <StackPanel Spacing="6" Margin="0,8,0,0"
                                IsVisible="{Binding !EsNuevoDocumento}">
                        <StackPanel IsVisible="{Binding PuedeVerTareasVinculadas}" Spacing="6">
                            <TextBlock Text="Tareas vinculadas" Classes="seccion" />
                            <ItemsControl ItemsSource="{Binding TareasVinculadas}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate x:DataType="dom:Tarea">
                                        <Border Classes="card" Margin="0,0,0,6" Padding="{DynamicResource PaddingCompacto}">
                                            <StackPanel Spacing="2">
                                                <TextBlock Text="{Binding Titulo}" FontWeight="SemiBold" />
                                                <TextBlock Text="{Binding Estado}" Classes="caption" />
                                            </StackPanel>
                                        </Border>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </StackPanel>
```

Sin mensaje de "lista vacía" a propósito (`IsVisible="{Binding !TareasVinculadas.Count}"` no compila como compiled binding — `Count` es `int`, no `bool`, y `!` sobre un `int` no tipa): mismo criterio que el resto de las listas de esta app (el hilo de Notas de `TareaFormView` y el de Eventos de este mismo `DocumentoFormView` tampoco muestran un mensaje de "vacío", un `ItemsControl` sin filas ya comunica "no hay nada").

`dom:Tarea` reusa el alias `xmlns:dom="using:StockApp.Domain.Entities"` ya declarado en la raíz del archivo (usado para `dom:EventoDocumento` más arriba en el mismo `DocumentoFormView.axaml`).

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~DocumentoFormViewModelTests"`
Expected: PASS (toda la clase, incluidos los 3 tests nuevos).

Run: `dotnet test tests/StockApp.Presentation.UiTests --filter "FullyQualifiedName~DocumentoFormViewGatesTests"`
Expected: PASS (la suite existente de gates de Documentos sigue verde).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Documentos/DocumentoFormViewModel.cs src/StockApp.Presentation/Views/Documentos/DocumentoFormView.axaml tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoFormViewModelTests.cs tests/StockApp.Presentation.UiTests/DocumentoFormViewGatesTests.cs
git commit -m "feat(documentos): sección Tareas vinculadas gateada por tareas.gestionar"
```

---

### Task 17: build completo + suite completa + commit final

**Files:** ninguno propio — task de verificación de cierre.

**Interfaces:** ninguna nueva.

- [ ] **Step 1: Build de la solución completa**

Run: `dotnet build GestionMunicipal.sln`
Expected: build exitoso, 0 errores (advertencias preexistentes tolerables). Usar la `.sln`, NUNCA varios `.csproj` sueltos en el mismo comando (falso verde conocido del repo).

- [ ] **Step 2: Suite completa, por proyecto y en secuencia**

Run: `dotnet test tests/StockApp.Domain.Tests`
Expected: PASS

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: PASS (contenedor `stockapp-pg` levantado)

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: PASS

Run: `dotnet test tests/StockApp.Api.Tests`
Expected: PASS (NUNCA en paralelo con `Application.Tests` — correr esta línea DESPUÉS de que la anterior termine)

Run: `dotnet test tests/StockApp.ApiClient.Tests`
Expected: PASS

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: PASS

Run: `dotnet test tests/StockApp.Presentation.UiTests`
Expected: PASS

- [ ] **Step 3: Verificación orgánica mínima (convención del repo)**

Levantar la API (`dotnet run --project src/StockApp.Api`) y el desktop (`dotnet run --project src/StockApp.Desktop`), loguearse como Admin, crear una tarea con los cinco clasificadores completos (incluido un expediente buscado por texto), verificar que aparece en la lista con las etiquetas en la tarjeta, abrir el detalle, reclasificar cambiando la zona, confirmar que la nota automática y el texto de solo lectura se actualizan sin recargar la pantalla, y por último abrir el expediente elegido y confirmar que la tarea aparece en "Tareas vinculadas".

- [ ] **Step 4: Commit final (si quedó algo suelto)**
```bash
git status
git add -A
git commit -m "chore(tareas): cierre de la clasificación de tareas — build y suite completa verdes"
```

(omitir este commit si `git status` no muestra cambios sin commitear — cada task anterior ya commiteó lo suyo).
