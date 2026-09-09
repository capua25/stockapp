# Catálogos de clasificación de Tareas — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar los cuatro catálogos administrables de clasificación de tareas (`Zona`, `DimensionTematica`, `OrganismoResponsable`, `OrigenFinanciamiento`) de punta a punta — Domain, persistencia EF, Application, Api, ApiClient y Presentation — más la pantalla `CatalogosTareaView` que los hospeda en un `TabControl`, copiando capa por capa el patrón ya validado de `Categoria`.

**Architecture:** Cuatro entidades planas (`Id:int`, `Nombre:string` único, `Activo:bool = true` con baja lógica), cada una con su propio servicio, repositorio, endpoints y ViewModels — cuatro verticales independientes que comparten el mismo molde exacto de `Categoria` (D2 del spec: se descartó una tabla genérica con discriminador para que la base garantice integridad referencial cuando el módulo Tareas, fuera de este plan, agregue sus FKs). La única pantalla nueva, `CatalogosTareaView`, hostea las cuatro listas en pestañas — mismo patrón que `MaestrosFinanzasView` con `FuenteFinanciamientoListView`/`RubroGastoListView`/`LineaPoaListView`. Diferencia deliberada frente a `CategoriaService`: estos cuatro servicios **NO invalidan `IVersionReportes`** (D14 del spec) — no reciben esa dependencia en el constructor, así que no hay nada que "corregir" copiando de más.

**Tech Stack:** .NET 10, C#, EF Core 10 + Npgsql/PostgreSQL, ASP.NET Core Minimal API, Avalonia 12.0.5 + CommunityToolkit.Mvvm, xUnit (Testcontainers/Postgres real para Infrastructure y Api, Moq en Application/ApiClient/Presentation.Tests).

**Spec:** docs/superpowers/specs/2026-09-08-tareas-clasificadores-design.md

## Global Constraints

- Rama de trabajo: crear `feat/tareas-catalogos` desde `main`. No pushear.
- Commits: conventional commits en español, uno por tarea como mínimo. **NUNCA agregar "Co-Authored-By" ni atribución a IA.**
- TDD estricto y sin atajos: escribir el test, **correrlo y verlo fallar**, implementar lo mínimo, correrlo y verlo pasar, commitear.
- **NUNCA correr `StockApp.Application.Tests` y `StockApp.Api.Tests` en paralelo**: colisionan por Testcontainers. Siempre secuencial.
- `StockApp.Infrastructure.Tests` corre contra PostgreSQL real; el contenedor `stockapp-pg` tiene que estar levantado.
- `dotnet build`/`dotnet test` con varios `.csproj` sueltos da falso verde en este repo (MSBuild descarta el segundo en silencio) — correr siempre por proyecto (`tests/StockApp.X.Tests`) o con la `.sln`, nunca `dotnet build src/*.csproj` a mano.
- `AccionAuditada` es **append-only**: nunca reordenar ni reutilizar valores. Este plan usa 60 a 71 (bloque de 3 valores — Alta/Baja/Modificacion — por catálogo, mismo orden que el bloque de `Categoria`).
- **Alcance cerrado**: este plan NO toca `Tarea`, `ITareaService`, el reporte-matriz ni `DocumentoAdministrativo`. Los cuatro catálogos nuevos quedan sin ningún consumidor dentro de Tareas — eso es intencional (D2/D14 del spec) y lo completa un plan posterior.
- **Sin permisos nuevos.** Los cuatro catálogos y la pantalla que los hospeda reutilizan `Permisos.GestionarTablasMaestras`, EXACTAMENTE el mismo permiso que ya gatea a `Categoria`/`Proveedor`/`UnidadMedida`. No se toca `Permisos.cs`.
- **Decisión de alcance (`ListarActivasAsync`)**: `CategoriaService.ListarActivasAsync` está gateado por `Permisos.GestionarProductos` — el permiso de QUIEN CONSUME el catálogo (Productos), no el de quien lo administra (`GestionarTablasMaestras`, que gatea el resto de los verbos). Verificado en `src/StockApp.Api/Endpoints/CategoriasEndpoints.cs`: `group.MapGet("/activas", ...).RequireAuthorization(Permisos.GestionarProductos)`. El futuro consumidor de estos cuatro catálogos es Tareas (el formulario de alta de tarea, fuera de alcance de este plan), y `Permisos.GestionarTareas` YA EXISTE en `Permisos.cs` — no es un permiso nuevo, solo se reutiliza. Gatear `ListarActivasAsync` con `GestionarTablasMaestras` (el permiso del ADMINISTRADOR del catálogo) sería un bug real: un Operador con `tareas.gestionar` pero sin `catalogo.maestras` — la combinación normal para quien solo carga tareas — recibiría 403 en los cuatro combos del formulario y no podría clasificar nada. Por eso los cuatro `ListarActivasAsync` (Application) y los cuatro `GET /activas` (Api) quedan gateados con `Permisos.GestionarTareas`, mismo patrón que `/categorias/activas` con `GestionarProductos`. El resto de los verbos (alta, modificar, baja, listar todas) siguen con `GestionarTablasMaestras`.
- El mecanismo de autorización HTTP (`PermisoAuthorizationHandler` + `AddPolicy` por cada `Permisos.X` en `Program.cs`) NO admite "uno de varios permisos": cada policy exige exactamente un permiso. Por eso el gate de `/activas` es un swap de permiso (`GestionarTareas` en vez de `GestionarTablasMaestras`), no una policy compuesta.
- **D14 (spec) — NO cachear.** `ZonaService`, `DimensionTematicaService`, `OrganismoResponsableService` y `OrigenFinanciamientoService` NO reciben `IVersionReportes` en el constructor y NO llaman a `.Invalidar()` en ningún método. Es deliberado: el reporte de tareas no usa cache. Si alguien lo "arregla" agregando la invalidación copiada de `CategoriaService`, está reintroduciendo comportamiento no deseado — cada servicio lleva un comentario XML explícito con esta nota.
- Las Views de Avalonia **no se auto-inicializan**: toda vista nueva engancha `DataContextChanged` para disparar su carga (bug recurrente del proyecto).
- `UnauthorizedAccessException` en los ViewModels de listado se captura **en silencio** en `CargarAsync` — el manejador central del 403 ya muestra el diálogo.
- Ubicación: los cuatro catálogos son familia de `Categoria`/`Proveedor`/`UnidadMedida`, NO del módulo Tareas — reutilizan las carpetas/namespaces YA EXISTENTES de esa familia, no uno nuevo `Tareas.Catalogo` o similar. Puntualmente (verificado contra `Categoria`, la plantilla): los servicios (`I*Service`/`*Service`) y todo Presentation (ViewModels/Views) van bajo `StockApp.Application.Catalogo`/`StockApp.Presentation.ViewModels.Catalogo`/`StockApp.Presentation.Views.Catalogo`, como `ICategoriaService`/`CategoriaService`; las interfaces de repositorio (`I*Repository`) van bajo `StockApp.Application.Interfaces`, como `ICategoriaRepository` — NO bajo `Catalogo` — y sus implementaciones EF bajo `StockApp.Infrastructure.Repositories`, como `CategoriaRepository`.
- **La lista de cada catálogo usa `DataGrid`, no `ListBox`.** `CategoriaListView.axaml` sigue usando `ListBox` hoy (no fue migrada el 2026-08-24), pero un catálogo de Nombre+Activo con columnas ES una tabla — mismo razonamiento que justificó migrar `ProveedorListView`/`UnidadMedidaListView`/`PagosGastoView` de `ListBox` a `DataGrid` ese día. El molde a replicar es `src/StockApp.Presentation/Views/Catalogo/UnidadMedidaListView.axaml` (confirmado `DataGrid` real hoy) — es el catálogo estructuralmente más parecido a los cuatro nuevos. El tema global `src/StockApp.Presentation/Themes/DataGrid.axaml` ya trae `RowHeight`, resize de columnas y el resto de los setters estandarizados: las vistas nuevas NO repiten esos setters, solo declaran columnas. Dos gotchas del repo al escribir XAML: los bindings son compilados vía `x:DataType` (un typo de binding es error de BUILD, no null silencioso) y un comentario XML con `--` rompe el build con AVLN1001 (evitar `--` dentro de comentarios `<!-- -->`).
- `DimensionTematica` se llama así en el código (D6 del spec); en pantalla el rótulo es "Dimensión" en singular (formulario, campo) y "Dimensiones" en plural (pestaña, listado) — mismo criterio que "Categoría"/"Categorías".
- **TRUNCATE hardcodeado en los fixtures de test — hay que tocarlo o los tests se contaminan entre sí.** Tanto `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs` (método privado `LimpiarTablas()`, se corre en el constructor antes de cada test de la clase `[Collection("Postgres")]`) como `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs` (método privado `LimpiarTablas()`, se corre en el constructor antes de cada test de la clase `[Collection("Api")]`) tienen una lista de nombres de tabla HARDCODEADA dentro de un `ctx.Database.ExecuteSqlRaw("TRUNCATE TABLE \"...\", \"...\" RESTART IDENTITY CASCADE;")`. Ninguna tabla nueva se agrega sola: si `"Zonas"`, `"DimensionesTematicas"`, `"OrganismosResponsables"` y `"OrigenesFinanciamiento"` no se suman a AMBAS listas, filas de un test sobreviven al siguiente (Testcontainers reusa el mismo Postgres entre tests de la misma collection) y los tests de repositorio/endpoint de estos catálogos empiezan a fallar de forma intermitente según el orden de ejecución — parece flaky y no lo es. El paso concreto que edita ambos archivos vive en la Task 2 (persistencia + migración), ANTES de que la Task 3 escriba el primer test de repositorio contra Postgres real — no al final del plan.

---

### Task 1: Entidades de dominio — `Zona`, `DimensionTematica`, `OrganismoResponsable`, `OrigenFinanciamiento`

**Files:**
- Create: `src/StockApp.Domain/Entities/Zona.cs`
- Create: `src/StockApp.Domain/Entities/DimensionTematica.cs`
- Create: `src/StockApp.Domain/Entities/OrganismoResponsable.cs`
- Create: `src/StockApp.Domain/Entities/OrigenFinanciamiento.cs`
- Test: `tests/StockApp.Domain.Tests/Entities/ClasificadoresTareaTests.cs`

**Interfaces:**
- Consumes: nada (POCOs sin dependencias, mismo molde que `src/StockApp.Domain/Entities/Categoria.cs`).
- Produces (consumido por Task 2 en adelante): `class Zona { int Id; string Nombre = ""; bool Activo = true; }`, `class DimensionTematica { int Id; string Nombre = ""; bool Activo = true; }`, `class OrganismoResponsable { int Id; string Nombre = ""; bool Activo = true; }`, `class OrigenFinanciamiento { int Id; string Nombre = ""; bool Activo = true; }`.

**Nota:** `Categoria` (la plantilla) no tiene test de Domain propio — es un POCO sin comportamiento y su único uso real se prueba a través de `CategoriaServiceTests`/`CategoriaRepositoryTests`. Acá se agrega un test mínimo de todas formas (verifica los defaults) para no romper el ciclo TDD de la Task 1: sin él, no habría nada que "falle primero".

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Domain.Tests/Entities/ClasificadoresTareaTests.cs`:

```csharp
using StockApp.Domain.Entities;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class ClasificadoresTareaTests
{
    [Fact]
    public void Zona_Defaults_NombreVacioYActivoTrue()
    {
        var zona = new Zona();

        Assert.Equal(0, zona.Id);
        Assert.Equal(string.Empty, zona.Nombre);
        Assert.True(zona.Activo);
    }

    [Fact]
    public void DimensionTematica_Defaults_NombreVacioYActivoTrue()
    {
        var dimension = new DimensionTematica();

        Assert.Equal(0, dimension.Id);
        Assert.Equal(string.Empty, dimension.Nombre);
        Assert.True(dimension.Activo);
    }

    [Fact]
    public void OrganismoResponsable_Defaults_NombreVacioYActivoTrue()
    {
        var organismo = new OrganismoResponsable();

        Assert.Equal(0, organismo.Id);
        Assert.Equal(string.Empty, organismo.Nombre);
        Assert.True(organismo.Activo);
    }

    [Fact]
    public void OrigenFinanciamiento_Defaults_NombreVacioYActivoTrue()
    {
        var origen = new OrigenFinanciamiento();

        Assert.Equal(0, origen.Id);
        Assert.Equal(string.Empty, origen.Nombre);
        Assert.True(origen.Activo);
    }

    [Fact]
    public void CuatroEntidades_PuedenAsignarNombreYDarDeBaja()
    {
        var zona = new Zona { Id = 1, Nombre = "Centro", Activo = false };
        var dimension = new DimensionTematica { Id = 2, Nombre = "Infraestructura", Activo = false };
        var organismo = new OrganismoResponsable { Id = 3, Nombre = "Intendencia de Colonia", Activo = false };
        var origen = new OrigenFinanciamiento { Id = 4, Nombre = "Presupuesto propio", Activo = false };

        Assert.Equal("Centro", zona.Nombre);
        Assert.Equal("Infraestructura", dimension.Nombre);
        Assert.Equal("Intendencia de Colonia", organismo.Nombre);
        Assert.Equal("Presupuesto propio", origen.Nombre);
        Assert.False(zona.Activo);
        Assert.False(dimension.Activo);
        Assert.False(organismo.Activo);
        Assert.False(origen.Activo);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Domain.Tests --filter FullyQualifiedName~ClasificadoresTareaTests`
Expected: FALLA de compilación — `StockApp.Domain.Entities.Zona`, `DimensionTematica`, `OrganismoResponsable` y `OrigenFinanciamiento` no existen.

- [ ] **Step 3: Implementación mínima**

Crear `src/StockApp.Domain/Entities/Zona.cs`:

```csharp
namespace StockApp.Domain.Entities;

public class Zona
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;  // obligatorio, único
    public bool Activo { get; set; } = true;             // baja lógica
}
```

Crear `src/StockApp.Domain/Entities/DimensionTematica.cs`:

```csharp
namespace StockApp.Domain.Entities;

public class DimensionTematica
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;  // obligatorio, único
    public bool Activo { get; set; } = true;             // baja lógica
}
```

Crear `src/StockApp.Domain/Entities/OrganismoResponsable.cs`:

```csharp
namespace StockApp.Domain.Entities;

public class OrganismoResponsable
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;  // obligatorio, único
    public bool Activo { get; set; } = true;             // baja lógica
}
```

Crear `src/StockApp.Domain/Entities/OrigenFinanciamiento.cs`:

```csharp
namespace StockApp.Domain.Entities;

public class OrigenFinanciamiento
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;  // obligatorio, único
    public bool Activo { get; set; } = true;             // baja lógica
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Domain.Tests --filter FullyQualifiedName~ClasificadoresTareaTests`
Expected: PASS — 5 tests verdes.

Run: `dotnet test tests/StockApp.Domain.Tests`
Expected: PASS — todos los tests en verde, sin regresiones.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Domain/Entities/Zona.cs \
        src/StockApp.Domain/Entities/DimensionTematica.cs \
        src/StockApp.Domain/Entities/OrganismoResponsable.cs \
        src/StockApp.Domain/Entities/OrigenFinanciamiento.cs \
        tests/StockApp.Domain.Tests/Entities/ClasificadoresTareaTests.cs
git commit -m "feat(catalogo): agrega entidades Zona, DimensionTematica, OrganismoResponsable y OrigenFinanciamiento"
```

---

### Task 2: Configuración EF en `AppDbContext` + migración + limpieza de fixtures de test

**Files:**
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs`
- Modify: `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs`
- Create: migración EF (generada por `dotnet ef migrations add`, no se escribe a mano)
- Test: `tests/StockApp.Infrastructure.Tests/Persistence/ClasificadoresTareaPersistenciaTests.cs`

**Interfaces:**
- Consumes: `Zona`, `DimensionTematica`, `OrganismoResponsable`, `OrigenFinanciamiento` (Task 1); `AppDbContext` (`src/StockApp.Infrastructure/Persistence/AppDbContext.cs`), su `OnModelCreating` centralizado (el repo NO usa clases `IEntityTypeConfiguration` separadas — molde del bloque `Categoria` en el mismo archivo).
- Produces (consumido por Task 3 en adelante): `DbSet<Zona> Zonas`, `DbSet<DimensionTematica> DimensionesTematicas`, `DbSet<OrganismoResponsable> OrganismosResponsables`, `DbSet<OrigenFinanciamiento> OrigenesFinanciamiento`, todos con índice único en `Nombre` y default `Activo = true`; migración `AgregaClasificadoresTarea` que crea las cuatro tablas.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Infrastructure.Tests/Persistence/ClasificadoresTareaPersistenciaTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

/// <summary>
/// Ejercita la configuración de AppDbContext (índice único + default Activo) para los cuatro
/// catálogos nuevos directamente contra los DbSet, ANTES de que exista ningún repositorio
/// (Task 3). Roundtrip básico + violación de unicidad, mismo criterio que
/// DocumentoAdministrativoIndiceUnicoTests del módulo de Documentos.
/// </summary>
public class ClasificadoresTareaPersistenciaTests : PostgresRepositoryTestBase
{
    public ClasificadoresTareaPersistenciaTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Zona_Roundtrip_ActivoPorDefectoTrue()
    {
        Context.Zonas.Add(new Zona { Nombre = "Centro" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var zona = await Context.Zonas.SingleAsync(z => z.Nombre == "Centro");
        Assert.True(zona.Activo);
    }

    [Fact]
    public async Task Zona_NombreDuplicado_ViolaIndiceUnico()
    {
        Context.Zonas.Add(new Zona { Nombre = "Centro" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.Zonas.Add(new Zona { Nombre = "Centro" });
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task DimensionTematica_Roundtrip_ActivoPorDefectoTrue()
    {
        Context.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Infraestructura" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var dimension = await Context.DimensionesTematicas.SingleAsync(d => d.Nombre == "Infraestructura");
        Assert.True(dimension.Activo);
    }

    [Fact]
    public async Task DimensionTematica_NombreDuplicado_ViolaIndiceUnico()
    {
        Context.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Infraestructura" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Infraestructura" });
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task OrganismoResponsable_Roundtrip_ActivoPorDefectoTrue()
    {
        Context.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Intendencia de Colonia" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var organismo = await Context.OrganismosResponsables.SingleAsync(o => o.Nombre == "Intendencia de Colonia");
        Assert.True(organismo.Activo);
    }

    [Fact]
    public async Task OrganismoResponsable_NombreDuplicado_ViolaIndiceUnico()
    {
        Context.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Intendencia de Colonia" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Intendencia de Colonia" });
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task OrigenFinanciamiento_Roundtrip_ActivoPorDefectoTrue()
    {
        Context.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Presupuesto propio" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var origen = await Context.OrigenesFinanciamiento.SingleAsync(o => o.Nombre == "Presupuesto propio");
        Assert.True(origen.Activo);
    }

    [Fact]
    public async Task OrigenFinanciamiento_NombreDuplicado_ViolaIndiceUnico()
    {
        Context.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Presupuesto propio" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        Context.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Presupuesto propio" });
        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~ClasificadoresTareaPersistenciaTests`
Expected: FALLA de compilación — `Context.Zonas`, `Context.DimensionesTematicas`, `Context.OrganismosResponsables` y `Context.OrigenesFinanciamiento` no existen todavía en `AppDbContext`.

- [ ] **Step 3: Agregar los `DbSet` y la configuración en `AppDbContext`**

```csharp
// src/StockApp.Infrastructure/Persistence/AppDbContext.cs
// Agregar junto al resto de DbSet<T>, después de DbSet<AsignacionPresupuestal> AsignacionesPresupuestales:

    public DbSet<Zona> Zonas => Set<Zona>();
    public DbSet<DimensionTematica> DimensionesTematicas => Set<DimensionTematica>();
    public DbSet<OrganismoResponsable> OrganismosResponsables => Set<OrganismoResponsable>();
    public DbSet<OrigenFinanciamiento> OrigenesFinanciamiento => Set<OrigenFinanciamiento>();
```

```csharp
// src/StockApp.Infrastructure/Persistence/AppDbContext.cs
// Agregar en OnModelCreating, después del bloque "── UnidadMedida ──" y antes de
// "── MovimientoStock ──":

        // ── Clasificadores de Tareas (spec 2026-09-08) ──────────────────────────
        // D14: catálogos administrables sin invalidación de cache de reportes — ver
        // ZonaService/DimensionTematicaService/OrganismoResponsableService/OrigenFinanciamientoService.
        modelBuilder.Entity<Zona>(e =>
        {
            e.Property(z => z.Nombre).IsRequired();
            e.HasIndex(z => z.Nombre).IsUnique();
            e.Property(z => z.Activo).HasDefaultValue(true);
        });

        modelBuilder.Entity<DimensionTematica>(e =>
        {
            e.Property(d => d.Nombre).IsRequired();
            e.HasIndex(d => d.Nombre).IsUnique();
            e.Property(d => d.Activo).HasDefaultValue(true);
        });

        modelBuilder.Entity<OrganismoResponsable>(e =>
        {
            e.Property(o => o.Nombre).IsRequired();
            e.HasIndex(o => o.Nombre).IsUnique();
            e.Property(o => o.Activo).HasDefaultValue(true);
        });

        modelBuilder.Entity<OrigenFinanciamiento>(e =>
        {
            e.Property(o => o.Nombre).IsRequired();
            e.HasIndex(o => o.Nombre).IsUnique();
            e.Property(o => o.Activo).HasDefaultValue(true);
        });
```

- [ ] **Step 4: Generar la migración**

Run: `dotnet ef migrations add AgregaClasificadoresTarea --project src/StockApp.Infrastructure --startup-project src/StockApp.Api`
Expected: crea `src/StockApp.Infrastructure/Migrations/<timestamp>_AgregaClasificadoresTarea.cs` + `.Designer.cs`, y actualiza `AppDbContextModelSnapshot.cs` con las cuatro tablas nuevas (`Zonas`, `DimensionesTematicas`, `OrganismosResponsables`, `OrigenesFinanciamiento`), cada una con índice único en `Nombre`.

- [ ] **Step 5: Sumar las cuatro tablas a los TRUNCATE hardcodeados de los fixtures de test**

`PostgresRepositoryTestBase.LimpiarTablas()` y `ApiTestBase.LimpiarTablas()` truncan una lista fija de tablas antes de cada test — una tabla que no está en la lista no se limpia entre tests y contamina la corrida siguiente dentro de la misma collection de Testcontainers. Ninguna de las dos listas se actualiza sola: hay que tocar ambas ACÁ, antes de que la Task 3 escriba el primer test de repositorio real.

```csharp
// tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs
// Reemplazar el cuerpo completo de LimpiarTablas():

    private void LimpiarTablas()
    {
        using var ctx = Fixture.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", \"Usuarios\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"AdjuntosContenido\", \"Adjuntos\", \"PagosGasto\", \"Gastos\", \"IngresosCaja\", " +
            "\"CorridasBackup\", \"NotasTarea\", \"Tareas\", \"LotesImportacion\", " +
            "\"PermisosUsuario\", \"AdjuntosDocumentoContenido\", \"AdjuntosDocumento\", " +
            "\"EventosDocumento\", \"DocumentosAdministrativos\", \"Zonas\", \"DimensionesTematicas\", " +
            "\"OrganismosResponsables\", \"OrigenesFinanciamiento\" RESTART IDENTITY CASCADE;");
    }
```

```csharp
// tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs
// Reemplazar el cuerpo completo de LimpiarTablas():

    private void LimpiarTablas()
    {
        using var ctx = Factory.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"AdjuntosContenido\", \"Adjuntos\", \"PagosGasto\", \"Gastos\", \"IngresosCaja\", " +
            "\"CorridasBackup\", \"Zonas\", \"DimensionesTematicas\", \"OrganismosResponsables\", " +
            "\"OrigenesFinanciamiento\", \"Usuarios\" RESTART IDENTITY CASCADE;");
    }
```

- [ ] **Step 6: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~ClasificadoresTareaPersistenciaTests`
Expected: los 8 tests en verde (Testcontainers levanta Postgres desde cero y aplica todas las migraciones, incluida la nueva).

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: todos los tests en verde — confirma que el `TRUNCATE` ampliado del Step 5 no rompió ningún test existente.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Infrastructure/Persistence/AppDbContext.cs \
        src/StockApp.Infrastructure/Migrations/ \
        tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs \
        tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs \
        tests/StockApp.Infrastructure.Tests/Persistence/ClasificadoresTareaPersistenciaTests.cs
git commit -m "feat(catalogo): agrega persistencia EF y migracion de los clasificadores de tareas"
```

---

### Task 3: Repositorios — interfaces + implementaciones EF de los cuatro catálogos

**Files:**
- Create: `src/StockApp.Application/Interfaces/IZonaRepository.cs`
- Create: `src/StockApp.Application/Interfaces/IDimensionTematicaRepository.cs`
- Create: `src/StockApp.Application/Interfaces/IOrganismoResponsableRepository.cs`
- Create: `src/StockApp.Application/Interfaces/IOrigenFinanciamientoRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/ZonaRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/DimensionTematicaRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/OrganismoResponsableRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/OrigenFinanciamientoRepository.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/ZonaRepositoryTests.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/DimensionTematicaRepositoryTests.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/OrganismoResponsableRepositoryTests.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/OrigenFinanciamientoRepositoryTests.cs`

**Interfaces:**
- Consumes: `AppDbContext.Zonas/DimensionesTematicas/OrganismosResponsables/OrigenesFinanciamiento` (Task 2); `Zona`/`DimensionTematica`/`OrganismoResponsable`/`OrigenFinanciamiento` (Task 1). Firmas calcadas EXACTAS de `ICategoriaRepository` (`src/StockApp.Application/Interfaces/ICategoriaRepository.cs`).
- Produces (consumido por Task 4): `IZonaRepository` (`ObtenerPorIdAsync`, `ListarTodasAsync`, `ExisteNombreAsync`, `AgregarAsync`, `ActualizarAsync`) y las tres interfaces análogas, cada una con su implementación EF.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/StockApp.Infrastructure.Tests/Repositories/ZonaRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class ZonaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly ZonaRepository _repo;

    public ZonaRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new ZonaRepository(Context);
    }

    private static Zona NuevaZona(string nombre, bool activo = true) =>
        new() { Nombre = nombre, Activo = activo };

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var zona = NuevaZona("Centro");
        var id = await _repo.AgregarAsync(zona);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Centro", found!.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasSinFiltroDeActivo()
    {
        await _repo.AgregarAsync(NuevaZona("Centro", activo: true));
        await _repo.AgregarAsync(NuevaZona("Norte", activo: true));
        await _repo.AgregarAsync(NuevaZona("Sur Inactiva", activo: false));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaOrdenadaPorNombre()
    {
        await _repo.AgregarAsync(NuevaZona("Zona Sur"));
        await _repo.AgregarAsync(NuevaZona("Centro"));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal("Centro", result[0].Nombre);
        Assert.Equal("Zona Sur", result[1].Nombre);
    }

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevaZona("Centro"));

        Assert.True(await _repo.ExisteNombreAsync("Centro"));
    }

    [Fact]
    public async Task ExisteNombreAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismaZona_RetornaFalse()
    {
        var zona = NuevaZona("Centro");
        var id = await _repo.AgregarAsync(zona);

        Assert.False(await _repo.ExisteNombreAsync("Centro", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_OtraTieneMismoNombre_RetornaTrue()
    {
        var zona1 = NuevaZona("Centro");
        var zona2 = NuevaZona("Norte");
        await _repo.AgregarAsync(zona1);
        var id2 = await _repo.AgregarAsync(zona2);

        Assert.True(await _repo.ExisteNombreAsync("Centro", excluyendoId: id2));
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var zona = NuevaZona("Centro");
        var id = await _repo.AgregarAsync(zona);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }

    [Fact]
    public async Task ActualizarAsync_ModificaNombre_Persiste()
    {
        var zona = NuevaZona("Nombre Original");
        var id = await _repo.AgregarAsync(zona);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Nombre Modificado";
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Nombre Modificado", updated!.Nombre);
    }
}
```

Crear `tests/StockApp.Infrastructure.Tests/Repositories/DimensionTematicaRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class DimensionTematicaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly DimensionTematicaRepository _repo;

    public DimensionTematicaRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new DimensionTematicaRepository(Context);
    }

    private static DimensionTematica NuevaDimension(string nombre, bool activo = true) =>
        new() { Nombre = nombre, Activo = activo };

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var dimension = NuevaDimension("Infraestructura");
        var id = await _repo.AgregarAsync(dimension);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Infraestructura", found!.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasSinFiltroDeActivo()
    {
        await _repo.AgregarAsync(NuevaDimension("Infraestructura", activo: true));
        await _repo.AgregarAsync(NuevaDimension("Tránsito", activo: true));
        await _repo.AgregarAsync(NuevaDimension("Discontinuada", activo: false));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaOrdenadaPorNombre()
    {
        await _repo.AgregarAsync(NuevaDimension("Tránsito"));
        await _repo.AgregarAsync(NuevaDimension("Infraestructura"));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal("Infraestructura", result[0].Nombre);
        Assert.Equal("Tránsito", result[1].Nombre);
    }

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevaDimension("Infraestructura"));

        Assert.True(await _repo.ExisteNombreAsync("Infraestructura"));
    }

    [Fact]
    public async Task ExisteNombreAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismaDimension_RetornaFalse()
    {
        var dimension = NuevaDimension("Infraestructura");
        var id = await _repo.AgregarAsync(dimension);

        Assert.False(await _repo.ExisteNombreAsync("Infraestructura", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_OtraTieneMismoNombre_RetornaTrue()
    {
        var dimension1 = NuevaDimension("Infraestructura");
        var dimension2 = NuevaDimension("Tránsito");
        await _repo.AgregarAsync(dimension1);
        var id2 = await _repo.AgregarAsync(dimension2);

        Assert.True(await _repo.ExisteNombreAsync("Infraestructura", excluyendoId: id2));
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var dimension = NuevaDimension("Infraestructura");
        var id = await _repo.AgregarAsync(dimension);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }

    [Fact]
    public async Task ActualizarAsync_ModificaNombre_Persiste()
    {
        var dimension = NuevaDimension("Nombre Original");
        var id = await _repo.AgregarAsync(dimension);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Nombre Modificado";
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Nombre Modificado", updated!.Nombre);
    }
}
```

Crear `tests/StockApp.Infrastructure.Tests/Repositories/OrganismoResponsableRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class OrganismoResponsableRepositoryTests : PostgresRepositoryTestBase
{
    private readonly OrganismoResponsableRepository _repo;

    public OrganismoResponsableRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new OrganismoResponsableRepository(Context);
    }

    private static OrganismoResponsable NuevoOrganismo(string nombre, bool activo = true) =>
        new() { Nombre = nombre, Activo = activo };

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var organismo = NuevoOrganismo("Intendencia de Colonia");
        var id = await _repo.AgregarAsync(organismo);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Intendencia de Colonia", found!.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasSinFiltroDeActivo()
    {
        await _repo.AgregarAsync(NuevoOrganismo("Intendencia de Colonia", activo: true));
        await _repo.AgregarAsync(NuevoOrganismo("Municipio de Carmelo", activo: true));
        await _repo.AgregarAsync(NuevoOrganismo("Organismo Inactivo", activo: false));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaOrdenadaPorNombre()
    {
        await _repo.AgregarAsync(NuevoOrganismo("Municipio de Carmelo"));
        await _repo.AgregarAsync(NuevoOrganismo("Intendencia de Colonia"));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal("Intendencia de Colonia", result[0].Nombre);
        Assert.Equal("Municipio de Carmelo", result[1].Nombre);
    }

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevoOrganismo("Intendencia de Colonia"));

        Assert.True(await _repo.ExisteNombreAsync("Intendencia de Colonia"));
    }

    [Fact]
    public async Task ExisteNombreAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismoOrganismo_RetornaFalse()
    {
        var organismo = NuevoOrganismo("Intendencia de Colonia");
        var id = await _repo.AgregarAsync(organismo);

        Assert.False(await _repo.ExisteNombreAsync("Intendencia de Colonia", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_OtroTieneMismoNombre_RetornaTrue()
    {
        var organismo1 = NuevoOrganismo("Intendencia de Colonia");
        var organismo2 = NuevoOrganismo("Municipio de Carmelo");
        await _repo.AgregarAsync(organismo1);
        var id2 = await _repo.AgregarAsync(organismo2);

        Assert.True(await _repo.ExisteNombreAsync("Intendencia de Colonia", excluyendoId: id2));
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var organismo = NuevoOrganismo("Intendencia de Colonia");
        var id = await _repo.AgregarAsync(organismo);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }

    [Fact]
    public async Task ActualizarAsync_ModificaNombre_Persiste()
    {
        var organismo = NuevoOrganismo("Nombre Original");
        var id = await _repo.AgregarAsync(organismo);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Nombre Modificado";
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Nombre Modificado", updated!.Nombre);
    }
}
```

Crear `tests/StockApp.Infrastructure.Tests/Repositories/OrigenFinanciamientoRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class OrigenFinanciamientoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly OrigenFinanciamientoRepository _repo;

    public OrigenFinanciamientoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new OrigenFinanciamientoRepository(Context);
    }

    private static OrigenFinanciamiento NuevoOrigen(string nombre, bool activo = true) =>
        new() { Nombre = nombre, Activo = activo };

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var origen = NuevoOrigen("Presupuesto propio");
        var id = await _repo.AgregarAsync(origen);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Presupuesto propio", found!.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasSinFiltroDeActivo()
    {
        await _repo.AgregarAsync(NuevoOrigen("Presupuesto propio", activo: true));
        await _repo.AgregarAsync(NuevoOrigen("Fondo nacional", activo: true));
        await _repo.AgregarAsync(NuevoOrigen("Convenio discontinuado", activo: false));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaOrdenadaPorNombre()
    {
        await _repo.AgregarAsync(NuevoOrigen("Fondo nacional"));
        await _repo.AgregarAsync(NuevoOrigen("Convenio"));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal("Convenio", result[0].Nombre);
        Assert.Equal("Fondo nacional", result[1].Nombre);
    }

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevoOrigen("Presupuesto propio"));

        Assert.True(await _repo.ExisteNombreAsync("Presupuesto propio"));
    }

    [Fact]
    public async Task ExisteNombreAsync_Inexistente_RetornaFalse()
    {
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismoOrigen_RetornaFalse()
    {
        var origen = NuevoOrigen("Presupuesto propio");
        var id = await _repo.AgregarAsync(origen);

        Assert.False(await _repo.ExisteNombreAsync("Presupuesto propio", excluyendoId: id));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_OtroTieneMismoNombre_RetornaTrue()
    {
        var origen1 = NuevoOrigen("Presupuesto propio");
        var origen2 = NuevoOrigen("Fondo nacional");
        await _repo.AgregarAsync(origen1);
        var id2 = await _repo.AgregarAsync(origen2);

        Assert.True(await _repo.ExisteNombreAsync("Presupuesto propio", excluyendoId: id2));
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_ActivoFalse_Persiste()
    {
        var origen = NuevoOrigen("Presupuesto propio");
        var id = await _repo.AgregarAsync(origen);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }

    [Fact]
    public async Task ActualizarAsync_ModificaNombre_Persiste()
    {
        var origen = NuevoOrigen("Nombre Original");
        var id = await _repo.AgregarAsync(origen);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Nombre Modificado";
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Nombre Modificado", updated!.Nombre);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ZonaRepositoryTests|FullyQualifiedName~DimensionTematicaRepositoryTests|FullyQualifiedName~OrganismoResponsableRepositoryTests|FullyQualifiedName~OrigenFinanciamientoRepositoryTests"`
Expected: FALLA de compilación — `StockApp.Application.Interfaces.IZonaRepository`, `IDimensionTematicaRepository`, `IOrganismoResponsableRepository`, `IOrigenFinanciamientoRepository` y sus cuatro implementaciones en `StockApp.Infrastructure.Repositories` no existen.

- [ ] **Step 3: Escribir las interfaces de repositorio**

Crear `src/StockApp.Application/Interfaces/IZonaRepository.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IZonaRepository
{
    Task<Zona?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<Zona>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(Zona zona);
    Task ActualizarAsync(Zona zona);
}
```

Crear `src/StockApp.Application/Interfaces/IDimensionTematicaRepository.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IDimensionTematicaRepository
{
    Task<DimensionTematica?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(DimensionTematica dimension);
    Task ActualizarAsync(DimensionTematica dimension);
}
```

Crear `src/StockApp.Application/Interfaces/IOrganismoResponsableRepository.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IOrganismoResponsableRepository
{
    Task<OrganismoResponsable?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(OrganismoResponsable organismo);
    Task ActualizarAsync(OrganismoResponsable organismo);
}
```

Crear `src/StockApp.Application/Interfaces/IOrigenFinanciamientoRepository.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IOrigenFinanciamientoRepository
{
    Task<OrigenFinanciamiento?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(OrigenFinanciamiento origen);
    Task ActualizarAsync(OrigenFinanciamiento origen);
}
```

- [ ] **Step 4: Implementación EF de los cuatro repositorios**

Crear `src/StockApp.Infrastructure/Repositories/ZonaRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class ZonaRepository : IZonaRepository
{
    private readonly AppDbContext _ctx;

    public ZonaRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<Zona?> ObtenerPorIdAsync(int id)
        => _ctx.Zonas.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<Zona>> ListarTodasAsync()
        => await _ctx.Zonas.OrderBy(z => z.Nombre).ToListAsync();

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.Zonas.AnyAsync(z => z.Nombre == nombre && z.Id != excluyendoId.Value)
            : _ctx.Zonas.AnyAsync(z => z.Nombre == nombre);

    public async Task<int> AgregarAsync(Zona zona)
    {
        _ctx.Zonas.Add(zona);
        await _ctx.SaveChangesAsync();
        return zona.Id;
    }

    public Task ActualizarAsync(Zona zona)
    {
        _ctx.Zonas.Update(zona);
        return _ctx.SaveChangesAsync();
    }
}
```

Crear `src/StockApp.Infrastructure/Repositories/DimensionTematicaRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class DimensionTematicaRepository : IDimensionTematicaRepository
{
    private readonly AppDbContext _ctx;

    public DimensionTematicaRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<DimensionTematica?> ObtenerPorIdAsync(int id)
        => _ctx.DimensionesTematicas.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync()
        => await _ctx.DimensionesTematicas.OrderBy(d => d.Nombre).ToListAsync();

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.DimensionesTematicas.AnyAsync(d => d.Nombre == nombre && d.Id != excluyendoId.Value)
            : _ctx.DimensionesTematicas.AnyAsync(d => d.Nombre == nombre);

    public async Task<int> AgregarAsync(DimensionTematica dimension)
    {
        _ctx.DimensionesTematicas.Add(dimension);
        await _ctx.SaveChangesAsync();
        return dimension.Id;
    }

    public Task ActualizarAsync(DimensionTematica dimension)
    {
        _ctx.DimensionesTematicas.Update(dimension);
        return _ctx.SaveChangesAsync();
    }
}
```

Crear `src/StockApp.Infrastructure/Repositories/OrganismoResponsableRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class OrganismoResponsableRepository : IOrganismoResponsableRepository
{
    private readonly AppDbContext _ctx;

    public OrganismoResponsableRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<OrganismoResponsable?> ObtenerPorIdAsync(int id)
        => _ctx.OrganismosResponsables.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync()
        => await _ctx.OrganismosResponsables.OrderBy(o => o.Nombre).ToListAsync();

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.OrganismosResponsables.AnyAsync(o => o.Nombre == nombre && o.Id != excluyendoId.Value)
            : _ctx.OrganismosResponsables.AnyAsync(o => o.Nombre == nombre);

    public async Task<int> AgregarAsync(OrganismoResponsable organismo)
    {
        _ctx.OrganismosResponsables.Add(organismo);
        await _ctx.SaveChangesAsync();
        return organismo.Id;
    }

    public Task ActualizarAsync(OrganismoResponsable organismo)
    {
        _ctx.OrganismosResponsables.Update(organismo);
        return _ctx.SaveChangesAsync();
    }
}
```

Crear `src/StockApp.Infrastructure/Repositories/OrigenFinanciamientoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class OrigenFinanciamientoRepository : IOrigenFinanciamientoRepository
{
    private readonly AppDbContext _ctx;

    public OrigenFinanciamientoRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<OrigenFinanciamiento?> ObtenerPorIdAsync(int id)
        => _ctx.OrigenesFinanciamiento.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync()
        => await _ctx.OrigenesFinanciamiento.OrderBy(o => o.Nombre).ToListAsync();

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.OrigenesFinanciamiento.AnyAsync(o => o.Nombre == nombre && o.Id != excluyendoId.Value)
            : _ctx.OrigenesFinanciamiento.AnyAsync(o => o.Nombre == nombre);

    public async Task<int> AgregarAsync(OrigenFinanciamiento origen)
    {
        _ctx.OrigenesFinanciamiento.Add(origen);
        await _ctx.SaveChangesAsync();
        return origen.Id;
    }

    public Task ActualizarAsync(OrigenFinanciamiento origen)
    {
        _ctx.OrigenesFinanciamiento.Update(origen);
        return _ctx.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ZonaRepositoryTests|FullyQualifiedName~DimensionTematicaRepositoryTests|FullyQualifiedName~OrganismoResponsableRepositoryTests|FullyQualifiedName~OrigenFinanciamientoRepositoryTests"`
Expected: los 36 tests en verde (9 por catálogo × 4).

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: todos los tests en verde (Tasks 1-3 acumuladas), sin regresiones.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application/Interfaces/IZonaRepository.cs \
        src/StockApp.Application/Interfaces/IDimensionTematicaRepository.cs \
        src/StockApp.Application/Interfaces/IOrganismoResponsableRepository.cs \
        src/StockApp.Application/Interfaces/IOrigenFinanciamientoRepository.cs \
        src/StockApp.Infrastructure/Repositories/ZonaRepository.cs \
        src/StockApp.Infrastructure/Repositories/DimensionTematicaRepository.cs \
        src/StockApp.Infrastructure/Repositories/OrganismoResponsableRepository.cs \
        src/StockApp.Infrastructure/Repositories/OrigenFinanciamientoRepository.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/ZonaRepositoryTests.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/DimensionTematicaRepositoryTests.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/OrganismoResponsableRepositoryTests.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/OrigenFinanciamientoRepositoryTests.cs
git commit -m "feat(catalogo): agrega repositorios EF de los clasificadores de tareas"
```

---

### Task 4: Valores de auditoría + servicios de Application (sin invalidación de cache, D14)

**Files:**
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs`
- Create: `src/StockApp.Application/Catalogo/IZonaService.cs`
- Create: `src/StockApp.Application/Catalogo/ZonaService.cs`
- Create: `src/StockApp.Application/Catalogo/IDimensionTematicaService.cs`
- Create: `src/StockApp.Application/Catalogo/DimensionTematicaService.cs`
- Create: `src/StockApp.Application/Catalogo/IOrganismoResponsableService.cs`
- Create: `src/StockApp.Application/Catalogo/OrganismoResponsableService.cs`
- Create: `src/StockApp.Application/Catalogo/IOrigenFinanciamientoService.cs`
- Create: `src/StockApp.Application/Catalogo/OrigenFinanciamientoService.cs`
- Test: `tests/StockApp.Application.Tests/Catalogo/ZonaServiceTests.cs`
- Test: `tests/StockApp.Application.Tests/Catalogo/DimensionTematicaServiceTests.cs`
- Test: `tests/StockApp.Application.Tests/Catalogo/OrganismoResponsableServiceTests.cs`
- Test: `tests/StockApp.Application.Tests/Catalogo/OrigenFinanciamientoServiceTests.cs`

**Interfaces:**
- Consumes: `IZonaRepository`/`IDimensionTematicaRepository`/`IOrganismoResponsableRepository`/`IOrigenFinanciamientoRepository` (Task 3); `Permisos.GestionarTablasMaestras` (`src/StockApp.Application/Authorization/Permisos.cs`, sin cambios); `ICurrentSession`, `IAuthorizationService`, `IAuditLogger` (`src/StockApp.Application/Interfaces/`); `ReglaDeNegocioException`/`EntidadNoEncontradaException` (`src/StockApp.Domain/Exceptions/`). Firmas calcadas EXACTAS de `ICategoriaService` (`src/StockApp.Application/Catalogo/ICategoriaService.cs`) — la única diferencia funcional es que el constructor NO recibe `IVersionReportes` (D14).
- Produces (consumido por Task 5 en adelante): `IZonaService`/`ZonaService` y las tres interfaces/clases análogas, cada una con `AltaAsync`, `ModificarAsync`, `BajaLogicaAsync`, `ListarTodasAsync`, `ListarActivasAsync` (gateada con `Permisos.GestionarTablasMaestras` — ver decisión en Global Constraints).

- [ ] **Step 1: Agregar los valores de `AccionAuditada`**

```csharp
// src/StockApp.Domain/Enums/AccionAuditada.cs
// Agregar DEBAJO de EdicionDocumento = 59, dentro del enum:

    // ── Clasificadores de Tareas — spec 2026-09-08 (append-only a partir de 60) ──
    AltaZona                        = 60,
    BajaZona                        = 61,
    ModificacionZona                = 62,
    AltaDimensionTematica           = 63,
    BajaDimensionTematica           = 64,
    ModificacionDimensionTematica   = 65,
    AltaOrganismoResponsable        = 66,
    BajaOrganismoResponsable        = 67,
    ModificacionOrganismoResponsable = 68,
    AltaOrigenFinanciamiento        = 69,
    BajaOrigenFinanciamiento        = 70,
    ModificacionOrigenFinanciamiento = 71,
```

- [ ] **Step 2: Escribir los tests que fallan**

Crear `tests/StockApp.Application.Tests/Catalogo/ZonaServiceTests.cs`:

```csharp
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Catalogo;

public class ZonaServiceTests
{
    private static (ZonaService svc,
                    Mock<IZonaRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var repo    = new Mock<IZonaRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        var sesion = new StockApp.Application.Auth.UsuarioSesion(idSesion, "usuario", rol, null);
        session.Setup(s => s.UsuarioActual).Returns(sesion);

        if (rol == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(session.Object, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(session.Object, Permisos.GestionarTablasMaestras))
                .Throws<UnauthorizedAccessException>();

        var svc = new ZonaService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, session, auth, audit);
    }

    // ─── Alta ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_NombreDuplicado_LanzaReglaDeNegocio()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Centro", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new Zona { Nombre = "Centro" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaZona()
    {
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Centro", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<Zona>())).ReturnsAsync(5);

        var id = await svc.AltaAsync(new Zona { Nombre = "Centro" });

        Assert.Equal(5, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaZona,
            "Zona", 5, It.Is<string>(d => d.Contains("Centro"))), Times.Once);
    }

    // ─── Modificar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_GranularPorCampo_AuditaModificacion()
    {
        var original = new Zona { Id = 1, Nombre = "Norte", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync("Norte Ampliado", 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new Zona { Id = 1, Nombre = "Norte Ampliado", Activo = true });

        repo.Verify(r => r.ActualizarAsync(It.Is<Zona>(z => z.Nombre == "Norte Ampliado")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionZona,
            "Zona", 1, It.Is<string>(d => d.Contains("Nombre"))), Times.Once);
    }

    // ─── Baja lógica ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaZona()
    {
        var z = new Zona { Id = 2, Nombre = "Sur", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(z);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<Zona>(x => x.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaZona,
            "Zona", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaReglaDeNegocio()
    {
        var z = new Zona { Id = 2, Nombre = "Sur", Activo = false };
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(z);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    // ─── Autorización ────────────────────────────────────────────────────────

    [Fact]
    public async Task Operador_GestionarTablasMaestras_LanzaUnauthorized()
    {
        var (svc, _, _, _, _) = Crear(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AltaAsync(new Zona { Nombre = "Sur" }));
    }

    [Fact]
    public async Task Admin_AltaExitosa_NuncaLanzaUnauthorized()
    {
        var (svc, repo, _, _, _) = Crear(RolUsuario.Admin);
        repo.Setup(r => r.ExisteNombreAsync(It.IsAny<string>(), null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<Zona>())).ReturnsAsync(1);

        var ex = await Record.ExceptionAsync(
            () => svc.AltaAsync(new Zona { Nombre = "Nueva" }));

        Assert.Null(ex);
    }

    // ─── ListarActivasAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ListarActivasAsync_FiltraInactivas()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<Zona>
        {
            new() { Id = 1, Nombre = "Centro", Activo = true },
            new() { Id = 2, Nombre = "Discontinuada", Activo = false },
        });

        var activas = await svc.ListarActivasAsync();

        Assert.Single(activas);
        Assert.Equal("Centro", activas[0].Nombre);
    }

    [Fact]
    public async Task ListarActivasAsync_Operador_NoLanzaUnauthorized()
    {
        // Gateada con GestionarTareas (el permiso del CONSUMIDOR del catálogo — el futuro
        // formulario de alta de tarea), no con GestionarTablasMaestras (el permiso del
        // ADMINISTRADOR del catálogo, que gatea el resto de los verbos). El mock de Crear()
        // solo hace throw para GestionarTablasMaestras: como ListarActivasAsync ahora verifica
        // un permiso distinto, un Operador sin permisos de administración igual puede listar
        // — mismo patrón que CategoriaServiceTests.ListarActivasAsync_Operador_NoLanzaUnauthorized
        // (gateado con GestionarProductos en vez de GestionarTablasMaestras).
        var (svc, repo, _, _, _) = Crear(RolUsuario.Operador);
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<Zona>());

        var ex = await Record.ExceptionAsync(() => svc.ListarActivasAsync());

        Assert.Null(ex);
    }

    // ─── EntidadNoEncontradaException ───────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_ZonaInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Zona?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new Zona { Id = 99, Nombre = "X" }));
    }

    [Fact]
    public async Task BajaLogicaAsync_ZonaInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Zona?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }

    // ─── D14: guardián de "no invalidación" ─────────────────────────────────

    [Fact]
    public void Constructor_NoRecibeIVersionReportes()
    {
        // Guardián estructural (D14 del spec 2026-09-08): a diferencia de CategoriaService,
        // ZonaService no depende de IVersionReportes porque el reporte de tareas no se cachea.
        // Si alguien "arregla" esto copiando de más desde CategoriaService, este test rompe
        // apenas se agregue el parámetro al constructor.
        var parametros = typeof(ZonaService).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parametros, p => p.ParameterType == typeof(IVersionReportes));
        Assert.Equal(4, parametros.Length);
    }
}
```

Crear `tests/StockApp.Application.Tests/Catalogo/DimensionTematicaServiceTests.cs`:

```csharp
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Catalogo;

public class DimensionTematicaServiceTests
{
    private static (DimensionTematicaService svc,
                    Mock<IDimensionTematicaRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var repo    = new Mock<IDimensionTematicaRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        var sesion = new StockApp.Application.Auth.UsuarioSesion(idSesion, "usuario", rol, null);
        session.Setup(s => s.UsuarioActual).Returns(sesion);

        if (rol == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(session.Object, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(session.Object, Permisos.GestionarTablasMaestras))
                .Throws<UnauthorizedAccessException>();

        var svc = new DimensionTematicaService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, session, auth, audit);
    }

    // ─── Alta ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_NombreDuplicado_LanzaReglaDeNegocio()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Infraestructura", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new DimensionTematica { Nombre = "Infraestructura" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaDimensionTematica()
    {
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Infraestructura", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<DimensionTematica>())).ReturnsAsync(5);

        var id = await svc.AltaAsync(new DimensionTematica { Nombre = "Infraestructura" });

        Assert.Equal(5, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaDimensionTematica,
            "DimensionTematica", 5, It.Is<string>(d => d.Contains("Infraestructura"))), Times.Once);
    }

    // ─── Modificar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_GranularPorCampo_AuditaModificacion()
    {
        var original = new DimensionTematica { Id = 1, Nombre = "Tránsito", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync("Tránsito y Movilidad", 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new DimensionTematica { Id = 1, Nombre = "Tránsito y Movilidad", Activo = true });

        repo.Verify(r => r.ActualizarAsync(It.Is<DimensionTematica>(d => d.Nombre == "Tránsito y Movilidad")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionDimensionTematica,
            "DimensionTematica", 1, It.Is<string>(d => d.Contains("Nombre"))), Times.Once);
    }

    // ─── Baja lógica ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaDimensionTematica()
    {
        var d = new DimensionTematica { Id = 2, Nombre = "Ambiente", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(d);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<DimensionTematica>(x => x.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaDimensionTematica,
            "DimensionTematica", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaReglaDeNegocio()
    {
        var d = new DimensionTematica { Id = 2, Nombre = "Ambiente", Activo = false };
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(d);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    // ─── Autorización ────────────────────────────────────────────────────────

    [Fact]
    public async Task Operador_GestionarTablasMaestras_LanzaUnauthorized()
    {
        var (svc, _, _, _, _) = Crear(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AltaAsync(new DimensionTematica { Nombre = "Ambiente" }));
    }

    [Fact]
    public async Task Admin_AltaExitosa_NuncaLanzaUnauthorized()
    {
        var (svc, repo, _, _, _) = Crear(RolUsuario.Admin);
        repo.Setup(r => r.ExisteNombreAsync(It.IsAny<string>(), null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<DimensionTematica>())).ReturnsAsync(1);

        var ex = await Record.ExceptionAsync(
            () => svc.AltaAsync(new DimensionTematica { Nombre = "Nueva" }));

        Assert.Null(ex);
    }

    // ─── ListarActivasAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ListarActivasAsync_FiltraInactivas()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<DimensionTematica>
        {
            new() { Id = 1, Nombre = "Infraestructura", Activo = true },
            new() { Id = 2, Nombre = "Discontinuada", Activo = false },
        });

        var activas = await svc.ListarActivasAsync();

        Assert.Single(activas);
        Assert.Equal("Infraestructura", activas[0].Nombre);
    }

    [Fact]
    public async Task ListarActivasAsync_Operador_NoLanzaUnauthorized()
    {
        // Gateada con GestionarTareas — ver nota completa en ZonaServiceTests.
        var (svc, repo, _, _, _) = Crear(RolUsuario.Operador);
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<DimensionTematica>());

        var ex = await Record.ExceptionAsync(() => svc.ListarActivasAsync());

        Assert.Null(ex);
    }

    // ─── EntidadNoEncontradaException ───────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_DimensionInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((DimensionTematica?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new DimensionTematica { Id = 99, Nombre = "X" }));
    }

    [Fact]
    public async Task BajaLogicaAsync_DimensionInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((DimensionTematica?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }

    // ─── D14: guardián de "no invalidación" ─────────────────────────────────

    [Fact]
    public void Constructor_NoRecibeIVersionReportes()
    {
        var parametros = typeof(DimensionTematicaService).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parametros, p => p.ParameterType == typeof(IVersionReportes));
        Assert.Equal(4, parametros.Length);
    }
}
```

Crear `tests/StockApp.Application.Tests/Catalogo/OrganismoResponsableServiceTests.cs`:

```csharp
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Catalogo;

public class OrganismoResponsableServiceTests
{
    private static (OrganismoResponsableService svc,
                    Mock<IOrganismoResponsableRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var repo    = new Mock<IOrganismoResponsableRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        var sesion = new StockApp.Application.Auth.UsuarioSesion(idSesion, "usuario", rol, null);
        session.Setup(s => s.UsuarioActual).Returns(sesion);

        if (rol == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(session.Object, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(session.Object, Permisos.GestionarTablasMaestras))
                .Throws<UnauthorizedAccessException>();

        var svc = new OrganismoResponsableService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, session, auth, audit);
    }

    // ─── Alta ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_NombreDuplicado_LanzaReglaDeNegocio()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Intendencia de Colonia", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new OrganismoResponsable { Nombre = "Intendencia de Colonia" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaOrganismoResponsable()
    {
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Intendencia de Colonia", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<OrganismoResponsable>())).ReturnsAsync(5);

        var id = await svc.AltaAsync(new OrganismoResponsable { Nombre = "Intendencia de Colonia" });

        Assert.Equal(5, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaOrganismoResponsable,
            "OrganismoResponsable", 5, It.Is<string>(d => d.Contains("Intendencia de Colonia"))), Times.Once);
    }

    // ─── Modificar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_GranularPorCampo_AuditaModificacion()
    {
        var original = new OrganismoResponsable { Id = 1, Nombre = "Municipio de Carmelo", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync("Municipio de Carmelo (Junta Local)", 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new OrganismoResponsable { Id = 1, Nombre = "Municipio de Carmelo (Junta Local)", Activo = true });

        repo.Verify(r => r.ActualizarAsync(It.Is<OrganismoResponsable>(o => o.Nombre == "Municipio de Carmelo (Junta Local)")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionOrganismoResponsable,
            "OrganismoResponsable", 1, It.Is<string>(d => d.Contains("Nombre"))), Times.Once);
    }

    // ─── Baja lógica ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaOrganismoResponsable()
    {
        var o = new OrganismoResponsable { Id = 2, Nombre = "Organismo Nacional", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(o);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<OrganismoResponsable>(x => x.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaOrganismoResponsable,
            "OrganismoResponsable", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaReglaDeNegocio()
    {
        var o = new OrganismoResponsable { Id = 2, Nombre = "Organismo Nacional", Activo = false };
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(o);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    // ─── Autorización ────────────────────────────────────────────────────────

    [Fact]
    public async Task Operador_GestionarTablasMaestras_LanzaUnauthorized()
    {
        var (svc, _, _, _, _) = Crear(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AltaAsync(new OrganismoResponsable { Nombre = "Organismo Nacional" }));
    }

    [Fact]
    public async Task Admin_AltaExitosa_NuncaLanzaUnauthorized()
    {
        var (svc, repo, _, _, _) = Crear(RolUsuario.Admin);
        repo.Setup(r => r.ExisteNombreAsync(It.IsAny<string>(), null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<OrganismoResponsable>())).ReturnsAsync(1);

        var ex = await Record.ExceptionAsync(
            () => svc.AltaAsync(new OrganismoResponsable { Nombre = "Nuevo" }));

        Assert.Null(ex);
    }

    // ─── ListarActivasAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ListarActivasAsync_FiltraInactivas()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<OrganismoResponsable>
        {
            new() { Id = 1, Nombre = "Intendencia de Colonia", Activo = true },
            new() { Id = 2, Nombre = "Discontinuado", Activo = false },
        });

        var activas = await svc.ListarActivasAsync();

        Assert.Single(activas);
        Assert.Equal("Intendencia de Colonia", activas[0].Nombre);
    }

    [Fact]
    public async Task ListarActivasAsync_Operador_NoLanzaUnauthorized()
    {
        // Gateada con GestionarTareas — ver nota completa en ZonaServiceTests.
        var (svc, repo, _, _, _) = Crear(RolUsuario.Operador);
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<OrganismoResponsable>());

        var ex = await Record.ExceptionAsync(() => svc.ListarActivasAsync());

        Assert.Null(ex);
    }

    // ─── EntidadNoEncontradaException ───────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_OrganismoInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((OrganismoResponsable?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new OrganismoResponsable { Id = 99, Nombre = "X" }));
    }

    [Fact]
    public async Task BajaLogicaAsync_OrganismoInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((OrganismoResponsable?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }

    // ─── D14: guardián de "no invalidación" ─────────────────────────────────

    [Fact]
    public void Constructor_NoRecibeIVersionReportes()
    {
        var parametros = typeof(OrganismoResponsableService).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parametros, p => p.ParameterType == typeof(IVersionReportes));
        Assert.Equal(4, parametros.Length);
    }
}
```

Crear `tests/StockApp.Application.Tests/Catalogo/OrigenFinanciamientoServiceTests.cs`:

```csharp
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Catalogo;

public class OrigenFinanciamientoServiceTests
{
    private static (OrigenFinanciamientoService svc,
                    Mock<IOrigenFinanciamientoRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1)
    {
        var repo    = new Mock<IOrigenFinanciamientoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        var sesion = new StockApp.Application.Auth.UsuarioSesion(idSesion, "usuario", rol, null);
        session.Setup(s => s.UsuarioActual).Returns(sesion);

        if (rol == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(session.Object, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(session.Object, Permisos.GestionarTablasMaestras))
                .Throws<UnauthorizedAccessException>();

        var svc = new OrigenFinanciamientoService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, session, auth, audit);
    }

    // ─── Alta ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_NombreDuplicado_LanzaReglaDeNegocio()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Presupuesto propio", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new OrigenFinanciamiento { Nombre = "Presupuesto propio" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaOrigenFinanciamiento()
    {
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Presupuesto propio", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<OrigenFinanciamiento>())).ReturnsAsync(5);

        var id = await svc.AltaAsync(new OrigenFinanciamiento { Nombre = "Presupuesto propio" });

        Assert.Equal(5, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaOrigenFinanciamiento,
            "OrigenFinanciamiento", 5, It.Is<string>(d => d.Contains("Presupuesto propio"))), Times.Once);
    }

    // ─── Modificar ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_GranularPorCampo_AuditaModificacion()
    {
        var original = new OrigenFinanciamiento { Id = 1, Nombre = "Fondo nacional", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync("Fondo nacional de infraestructura", 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new OrigenFinanciamiento { Id = 1, Nombre = "Fondo nacional de infraestructura", Activo = true });

        repo.Verify(r => r.ActualizarAsync(It.Is<OrigenFinanciamiento>(o => o.Nombre == "Fondo nacional de infraestructura")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionOrigenFinanciamiento,
            "OrigenFinanciamiento", 1, It.Is<string>(d => d.Contains("Nombre"))), Times.Once);
    }

    // ─── Baja lógica ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaOrigenFinanciamiento()
    {
        var o = new OrigenFinanciamiento { Id = 2, Nombre = "Convenio", Activo = true };
        var (svc, repo, _, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(o);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<OrigenFinanciamiento>(x => x.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaOrigenFinanciamiento,
            "OrigenFinanciamiento", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaReglaDeNegocio()
    {
        var o = new OrigenFinanciamiento { Id = 2, Nombre = "Convenio", Activo = false };
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(o);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    // ─── Autorización ────────────────────────────────────────────────────────

    [Fact]
    public async Task Operador_GestionarTablasMaestras_LanzaUnauthorized()
    {
        var (svc, _, _, _, _) = Crear(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AltaAsync(new OrigenFinanciamiento { Nombre = "Convenio" }));
    }

    [Fact]
    public async Task Admin_AltaExitosa_NuncaLanzaUnauthorized()
    {
        var (svc, repo, _, _, _) = Crear(RolUsuario.Admin);
        repo.Setup(r => r.ExisteNombreAsync(It.IsAny<string>(), null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<OrigenFinanciamiento>())).ReturnsAsync(1);

        var ex = await Record.ExceptionAsync(
            () => svc.AltaAsync(new OrigenFinanciamiento { Nombre = "Nuevo" }));

        Assert.Null(ex);
    }

    // ─── ListarActivasAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task ListarActivasAsync_FiltraInactivas()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<OrigenFinanciamiento>
        {
            new() { Id = 1, Nombre = "Presupuesto propio", Activo = true },
            new() { Id = 2, Nombre = "Discontinuado", Activo = false },
        });

        var activas = await svc.ListarActivasAsync();

        Assert.Single(activas);
        Assert.Equal("Presupuesto propio", activas[0].Nombre);
    }

    [Fact]
    public async Task ListarActivasAsync_Operador_NoLanzaUnauthorized()
    {
        // Gateada con GestionarTareas — ver nota completa en ZonaServiceTests.
        var (svc, repo, _, _, _) = Crear(RolUsuario.Operador);
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<OrigenFinanciamiento>());

        var ex = await Record.ExceptionAsync(() => svc.ListarActivasAsync());

        Assert.Null(ex);
    }

    // ─── EntidadNoEncontradaException ───────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_OrigenInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((OrigenFinanciamiento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new OrigenFinanciamiento { Id = 99, Nombre = "X" }));
    }

    [Fact]
    public async Task BajaLogicaAsync_OrigenInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((OrigenFinanciamiento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.BajaLogicaAsync(99));
    }

    // ─── D14: guardián de "no invalidación" ─────────────────────────────────

    [Fact]
    public void Constructor_NoRecibeIVersionReportes()
    {
        var parametros = typeof(OrigenFinanciamientoService).GetConstructors().Single().GetParameters();

        Assert.DoesNotContain(parametros, p => p.ParameterType == typeof(IVersionReportes));
        Assert.Equal(4, parametros.Length);
    }
}
```

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ZonaServiceTests|FullyQualifiedName~DimensionTematicaServiceTests|FullyQualifiedName~OrganismoResponsableServiceTests|FullyQualifiedName~OrigenFinanciamientoServiceTests"`
Expected: FALLA de compilación — `IZonaService`/`ZonaService` y las tres interfaces/clases análogas no existen; `AccionAuditada.AltaZona` (y el resto de los 12 valores nuevos) tampoco.

- [ ] **Step 4: Escribir las interfaces de servicio**

Crear `src/StockApp.Application/Catalogo/IZonaService.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IZonaService
{
    Task<int> AltaAsync(Zona zona);
    Task ModificarAsync(Zona zona);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<Zona>> ListarTodasAsync();

    /// <summary>
    /// Zonas activas disponibles para selección. Gateada con GestionarTareas — el permiso de
    /// QUIEN CONSUME el catálogo (el futuro formulario de alta de tarea), no GestionarTablasMaestras
    /// (quien lo administra). Mismo patrón que ICategoriaService.ListarActivasAsync con
    /// GestionarProductos (ver Global Constraints del plan 2026-09-08-tareas-catalogos.md).
    /// </summary>
    Task<IReadOnlyList<Zona>> ListarActivasAsync();
}
```

Crear `src/StockApp.Application/Catalogo/IDimensionTematicaService.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IDimensionTematicaService
{
    Task<int> AltaAsync(DimensionTematica dimension);
    Task ModificarAsync(DimensionTematica dimension);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync();

    /// <summary>
    /// Dimensiones activas disponibles para selección. Gateada con GestionarTareas —
    /// ver nota en IZonaService.
    /// </summary>
    Task<IReadOnlyList<DimensionTematica>> ListarActivasAsync();
}
```

Crear `src/StockApp.Application/Catalogo/IOrganismoResponsableService.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IOrganismoResponsableService
{
    Task<int> AltaAsync(OrganismoResponsable organismo);
    Task ModificarAsync(OrganismoResponsable organismo);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync();

    /// <summary>
    /// Organismos activos disponibles para selección. Gateada con GestionarTareas —
    /// ver nota en IZonaService.
    /// </summary>
    Task<IReadOnlyList<OrganismoResponsable>> ListarActivasAsync();
}
```

Crear `src/StockApp.Application/Catalogo/IOrigenFinanciamientoService.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IOrigenFinanciamientoService
{
    Task<int> AltaAsync(OrigenFinanciamiento origen);
    Task ModificarAsync(OrigenFinanciamiento origen);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync();

    /// <summary>
    /// Orígenes activos disponibles para selección. Gateada con GestionarTareas —
    /// ver nota en IZonaService.
    /// </summary>
    Task<IReadOnlyList<OrigenFinanciamiento>> ListarActivasAsync();
}
```

- [ ] **Step 5: Implementación de los cuatro servicios**

Crear `src/StockApp.Application/Catalogo/ZonaService.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de Zona. Solo Admin (GestionarTablasMaestras). Baja lógica con Activo=false.
/// D14 (spec 2026-09-08): a diferencia de CategoriaService, NO invalida IVersionReportes —
/// el reporte de tareas no se cachea, así que este servicio ni siquiera recibe esa
/// dependencia en el constructor. No agregarla "para ser consistente con Categoria".
/// </summary>
public class ZonaService : IZonaService
{
    private readonly IZonaRepository       _repo;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger          _audit;

    public ZonaService(
        IZonaRepository       repo,
        ICurrentSession       session,
        IAuthorizationService auth,
        IAuditLogger          audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(Zona zona)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        if (string.IsNullOrWhiteSpace(zona.Nombre))
            throw new ArgumentException("El nombre de la zona es obligatorio.");

        if (await _repo.ExisteNombreAsync(zona.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe una zona con el nombre '{zona.Nombre}'.");

        var id = await _repo.AgregarAsync(zona);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaZona,
            "Zona", id,
            $"Nombre: {zona.Nombre}");

        return id;
    }

    public async Task ModificarAsync(Zona zona)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var original = await _repo.ObtenerPorIdAsync(zona.Id)
            ?? throw new EntidadNoEncontradaException($"Zona {zona.Id} no encontrada.");

        if (original.Nombre != zona.Nombre
            && await _repo.ExisteNombreAsync(zona.Nombre, zona.Id))
            throw new ReglaDeNegocioException($"Ya existe una zona con el nombre '{zona.Nombre}'.");

        var cambios = new List<string>();
        if (original.Nombre != zona.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {zona.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Nombre = zona.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionZona,
            "Zona", zona.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var zona = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Zona {id} no encontrada.");

        if (!zona.Activo)
            throw new ReglaDeNegocioException($"La zona {id} ya está inactiva.");

        zona.Activo = false;
        await _repo.ActualizarAsync(zona);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaZona,
            "Zona", id,
            $"Baja lógica de '{zona.Nombre}'");
    }

    public async Task<IReadOnlyList<Zona>> ListarTodasAsync()
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<Zona>> ListarActivasAsync()
    {
        // Gateada con GestionarTareas (permiso del consumidor), NO con GestionarTablasMaestras
        // (permiso del administrador) — mismo patrón que CategoriaService.ListarActivasAsync
        // con GestionarProductos. Ver decisión en Global Constraints del plan.
        _auth.Verificar(_session, Permisos.GestionarTareas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(z => z.Activo).ToList();
    }
}
```

Crear `src/StockApp.Application/Catalogo/DimensionTematicaService.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de DimensionTematica. Solo Admin (GestionarTablasMaestras). Baja lógica con Activo=false.
/// D14 (spec 2026-09-08): NO invalida IVersionReportes — ver ZonaService para la nota completa.
/// </summary>
public class DimensionTematicaService : IDimensionTematicaService
{
    private readonly IDimensionTematicaRepository _repo;
    private readonly ICurrentSession               _session;
    private readonly IAuthorizationService         _auth;
    private readonly IAuditLogger                  _audit;

    public DimensionTematicaService(
        IDimensionTematicaRepository repo,
        ICurrentSession              session,
        IAuthorizationService        auth,
        IAuditLogger                 audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(DimensionTematica dimension)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        if (string.IsNullOrWhiteSpace(dimension.Nombre))
            throw new ArgumentException("El nombre de la dimensión es obligatorio.");

        if (await _repo.ExisteNombreAsync(dimension.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe una dimensión con el nombre '{dimension.Nombre}'.");

        var id = await _repo.AgregarAsync(dimension);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaDimensionTematica,
            "DimensionTematica", id,
            $"Nombre: {dimension.Nombre}");

        return id;
    }

    public async Task ModificarAsync(DimensionTematica dimension)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var original = await _repo.ObtenerPorIdAsync(dimension.Id)
            ?? throw new EntidadNoEncontradaException($"Dimensión {dimension.Id} no encontrada.");

        if (original.Nombre != dimension.Nombre
            && await _repo.ExisteNombreAsync(dimension.Nombre, dimension.Id))
            throw new ReglaDeNegocioException($"Ya existe una dimensión con el nombre '{dimension.Nombre}'.");

        var cambios = new List<string>();
        if (original.Nombre != dimension.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {dimension.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Nombre = dimension.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionDimensionTematica,
            "DimensionTematica", dimension.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var dimension = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Dimensión {id} no encontrada.");

        if (!dimension.Activo)
            throw new ReglaDeNegocioException($"La dimensión {id} ya está inactiva.");

        dimension.Activo = false;
        await _repo.ActualizarAsync(dimension);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaDimensionTematica,
            "DimensionTematica", id,
            $"Baja lógica de '{dimension.Nombre}'");
    }

    public async Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync()
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<DimensionTematica>> ListarActivasAsync()
    {
        // Gateada con GestionarTareas — ver nota en ZonaService.ListarActivasAsync.
        _auth.Verificar(_session, Permisos.GestionarTareas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(d => d.Activo).ToList();
    }
}
```

Crear `src/StockApp.Application/Catalogo/OrganismoResponsableService.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de OrganismoResponsable. Solo Admin (GestionarTablasMaestras). Baja lógica con Activo=false.
/// D14 (spec 2026-09-08): NO invalida IVersionReportes — ver ZonaService para la nota completa.
/// </summary>
public class OrganismoResponsableService : IOrganismoResponsableService
{
    private readonly IOrganismoResponsableRepository _repo;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public OrganismoResponsableService(
        IOrganismoResponsableRepository repo,
        ICurrentSession                 session,
        IAuthorizationService           auth,
        IAuditLogger                    audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(OrganismoResponsable organismo)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        if (string.IsNullOrWhiteSpace(organismo.Nombre))
            throw new ArgumentException("El nombre del organismo es obligatorio.");

        if (await _repo.ExisteNombreAsync(organismo.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe un organismo con el nombre '{organismo.Nombre}'.");

        var id = await _repo.AgregarAsync(organismo);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaOrganismoResponsable,
            "OrganismoResponsable", id,
            $"Nombre: {organismo.Nombre}");

        return id;
    }

    public async Task ModificarAsync(OrganismoResponsable organismo)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var original = await _repo.ObtenerPorIdAsync(organismo.Id)
            ?? throw new EntidadNoEncontradaException($"Organismo {organismo.Id} no encontrado.");

        if (original.Nombre != organismo.Nombre
            && await _repo.ExisteNombreAsync(organismo.Nombre, organismo.Id))
            throw new ReglaDeNegocioException($"Ya existe un organismo con el nombre '{organismo.Nombre}'.");

        var cambios = new List<string>();
        if (original.Nombre != organismo.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {organismo.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Nombre = organismo.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionOrganismoResponsable,
            "OrganismoResponsable", organismo.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var organismo = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Organismo {id} no encontrado.");

        if (!organismo.Activo)
            throw new ReglaDeNegocioException($"El organismo {id} ya está inactivo.");

        organismo.Activo = false;
        await _repo.ActualizarAsync(organismo);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaOrganismoResponsable,
            "OrganismoResponsable", id,
            $"Baja lógica de '{organismo.Nombre}'");
    }

    public async Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync()
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<OrganismoResponsable>> ListarActivasAsync()
    {
        // Gateada con GestionarTareas — ver nota en ZonaService.ListarActivasAsync.
        _auth.Verificar(_session, Permisos.GestionarTareas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(o => o.Activo).ToList();
    }
}
```

Crear `src/StockApp.Application/Catalogo/OrigenFinanciamientoService.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Catalogo;

/// <summary>
/// ABM de OrigenFinanciamiento. Solo Admin (GestionarTablasMaestras). Baja lógica con Activo=false.
/// D3 (spec 2026-09-08): catálogo propio, NO se reusa FuenteFinanciamiento de Finanzas.
/// D14: NO invalida IVersionReportes — ver ZonaService para la nota completa.
/// </summary>
public class OrigenFinanciamientoService : IOrigenFinanciamientoService
{
    private readonly IOrigenFinanciamientoRepository _repo;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public OrigenFinanciamientoService(
        IOrigenFinanciamientoRepository repo,
        ICurrentSession                 session,
        IAuthorizationService           auth,
        IAuditLogger                    audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(OrigenFinanciamiento origen)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        if (string.IsNullOrWhiteSpace(origen.Nombre))
            throw new ArgumentException("El nombre del origen de financiamiento es obligatorio.");

        if (await _repo.ExisteNombreAsync(origen.Nombre, null))
            throw new ReglaDeNegocioException($"Ya existe un origen de financiamiento con el nombre '{origen.Nombre}'.");

        var id = await _repo.AgregarAsync(origen);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaOrigenFinanciamiento,
            "OrigenFinanciamiento", id,
            $"Nombre: {origen.Nombre}");

        return id;
    }

    public async Task ModificarAsync(OrigenFinanciamiento origen)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var original = await _repo.ObtenerPorIdAsync(origen.Id)
            ?? throw new EntidadNoEncontradaException($"Origen de financiamiento {origen.Id} no encontrado.");

        if (original.Nombre != origen.Nombre
            && await _repo.ExisteNombreAsync(origen.Nombre, origen.Id))
            throw new ReglaDeNegocioException($"Ya existe un origen de financiamiento con el nombre '{origen.Nombre}'.");

        var cambios = new List<string>();
        if (original.Nombre != origen.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {origen.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Nombre = origen.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionOrigenFinanciamiento,
            "OrigenFinanciamiento", origen.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);

        var origen = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Origen de financiamiento {id} no encontrado.");

        if (!origen.Activo)
            throw new ReglaDeNegocioException($"El origen de financiamiento {id} ya está inactivo.");

        origen.Activo = false;
        await _repo.ActualizarAsync(origen);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaOrigenFinanciamiento,
            "OrigenFinanciamiento", id,
            $"Baja lógica de '{origen.Nombre}'");
    }

    public async Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync()
    {
        _auth.Verificar(_session, Permisos.GestionarTablasMaestras);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<OrigenFinanciamiento>> ListarActivasAsync()
    {
        // Gateada con GestionarTareas — ver nota en ZonaService.ListarActivasAsync.
        _auth.Verificar(_session, Permisos.GestionarTareas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(o => o.Activo).ToList();
    }
}
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ZonaServiceTests|FullyQualifiedName~DimensionTematicaServiceTests|FullyQualifiedName~OrganismoResponsableServiceTests|FullyQualifiedName~OrigenFinanciamientoServiceTests"`
Expected: los 48 tests en verde (12 por catálogo × 4).

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: todos los tests en verde, sin regresiones sobre el resto del módulo.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Domain/Enums/AccionAuditada.cs \
        src/StockApp.Application/Catalogo/IZonaService.cs \
        src/StockApp.Application/Catalogo/ZonaService.cs \
        src/StockApp.Application/Catalogo/IDimensionTematicaService.cs \
        src/StockApp.Application/Catalogo/DimensionTematicaService.cs \
        src/StockApp.Application/Catalogo/IOrganismoResponsableService.cs \
        src/StockApp.Application/Catalogo/OrganismoResponsableService.cs \
        src/StockApp.Application/Catalogo/IOrigenFinanciamientoService.cs \
        src/StockApp.Application/Catalogo/OrigenFinanciamientoService.cs \
        tests/StockApp.Application.Tests/Catalogo/ZonaServiceTests.cs \
        tests/StockApp.Application.Tests/Catalogo/DimensionTematicaServiceTests.cs \
        tests/StockApp.Application.Tests/Catalogo/OrganismoResponsableServiceTests.cs \
        tests/StockApp.Application.Tests/Catalogo/OrigenFinanciamientoServiceTests.cs
git commit -m "feat(catalogo): agrega servicios de Application de los clasificadores de tareas sin invalidacion de cache"
```

---

### Task 5: Api — endpoints de los cuatro catálogos + registro en `Program.cs`

**Files:**
- Create: `src/StockApp.Api/Endpoints/ZonasEndpoints.cs`
- Create: `src/StockApp.Api/Endpoints/DimensionesTematicasEndpoints.cs`
- Create: `src/StockApp.Api/Endpoints/OrganismosResponsablesEndpoints.cs`
- Create: `src/StockApp.Api/Endpoints/OrigenesFinanciamientoEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/ZonasEndpointTests.cs`
- Test: `tests/StockApp.Api.Tests/DimensionesTematicasEndpointTests.cs`
- Test: `tests/StockApp.Api.Tests/OrganismosResponsablesEndpointTests.cs`
- Test: `tests/StockApp.Api.Tests/OrigenesFinanciamientoEndpointTests.cs`

**Interfaces:**
- Consumes: `IZonaService`/`IDimensionTematicaService`/`IOrganismoResponsableService`/`IOrigenFinanciamientoService` (Task 4); `IZonaRepository`/etc. + `ZonaRepository`/etc. (Task 3); `Permisos.GestionarTablasMaestras`. Molde EXACTO de `CategoriasEndpoints` (`src/StockApp.Api/Endpoints/CategoriasEndpoints.cs`).
- Produces (consumido por Task 6): rutas `GET/POST /zonas`, `PUT/DELETE /zonas/{id}`, `GET /zonas/activas` y las tres familias análogas bajo `/dimensiones-tematicas`, `/organismos-responsables`, `/origenes-financiamiento`, todas devolviendo `{Id, Nombre, Activo}`.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/StockApp.Api.Tests/ZonasEndpointTests.cs`:

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

public class ZonasEndpointTests : ApiTestBase
{
    public ZonasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetZonas_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/zonas");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetZonas_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/zonas");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetZonas_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Zonas.Add(new Zona { Nombre = "Centro", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/zonas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var zonas = await response.Content.ReadFromJsonAsync<List<ZonaDto>>();
        Assert.Contains(zonas!, z => z.Nombre == "Centro");
    }

    [Fact]
    public async Task PostZonas_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/zonas", new CrearZonaRequest("Norte"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.Zonas.AnyAsync(z => z.Nombre == "Norte"));
    }

    [Fact]
    public async Task PostZonas_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/zonas", new CrearZonaRequest("Norte"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostZonas_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.Zonas.Add(new Zona { Nombre = "Sur", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/zonas", new CrearZonaRequest("Sur"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutZonas_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var zona = new Zona { Nombre = "Original", Activo = true };
        ctx.Zonas.Add(zona);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/zonas/{zona.Id}", new ModificarZonaRequest("Modificada"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteZonas_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var zona = new Zona { Nombre = "Para Baja", Activo = true };
        ctx.Zonas.Add(zona);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/zonas/{zona.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizada = await verificacion.Zonas.SingleAsync(z => z.Id == zona.Id);
        Assert.False(actualizada.Activo);
    }

    [Fact]
    public async Task GetZonasActivas_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Zonas.Add(new Zona { Nombre = "Activa", Activo = true });
        ctx.Zonas.Add(new Zona { Nombre = "Inactiva", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/zonas/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var zonas = await response.Content.ReadFromJsonAsync<List<ZonaDto>>();
        Assert.Contains(zonas!, z => z.Nombre == "Activa");
        Assert.DoesNotContain(zonas!, z => z.Nombre == "Inactiva");
    }

    [Fact]
    public async Task GetZonasActivas_ConTokenOperador_Devuelve200()
    {
        // Gateada con GestionarTareas, que SÍ está entre los permisos iniciales por defecto de
        // un Operador (AuthorizationService.PermisosInicialesOperador) — a diferencia de
        // GestionarTablasMaestras, que NO lo está. Por eso acá se siembra un Operador real
        // (SeedOperadorConTokenAsync, permisos por defecto) en vez de usar TokenOperador() (un
        // JWT para un usuarioId=2 que no existe en la base: con PoblarPermisosMiddleware
        // puesto, eso da 403 fail-closed sin importar el permiso). Mismo patrón que
        // CategoriasEndpointTests.GetCategoriasActivas_ConTokenOperador_Devuelve200.
        await using var ctx = Factory.CrearContexto();
        ctx.Zonas.Add(new Zona { Nombre = "Activa", Activo = true });
        await ctx.SaveChangesAsync();
        var (_, token) = await DatosDePrueba.SeedOperadorConTokenAsync(
            ctx, Factory.Services.GetRequiredService<IJwtTokenService>(), "operador.test");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/zonas/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var zonas = await response.Content.ReadFromJsonAsync<List<ZonaDto>>();
        Assert.Contains(zonas!, z => z.Nombre == "Activa");
    }
}
```

Crear `tests/StockApp.Api.Tests/DimensionesTematicasEndpointTests.cs`:

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

public class DimensionesTematicasEndpointTests : ApiTestBase
{
    public DimensionesTematicasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetDimensiones_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/dimensiones-tematicas");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDimensiones_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/dimensiones-tematicas");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetDimensiones_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Infraestructura", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/dimensiones-tematicas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dimensiones = await response.Content.ReadFromJsonAsync<List<DimensionTematicaDto>>();
        Assert.Contains(dimensiones!, d => d.Nombre == "Infraestructura");
    }

    [Fact]
    public async Task PostDimensiones_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/dimensiones-tematicas", new CrearDimensionTematicaRequest("Tránsito"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.DimensionesTematicas.AnyAsync(d => d.Nombre == "Tránsito"));
    }

    [Fact]
    public async Task PostDimensiones_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/dimensiones-tematicas", new CrearDimensionTematicaRequest("Tránsito"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostDimensiones_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Ambiente", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/dimensiones-tematicas", new CrearDimensionTematicaRequest("Ambiente"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutDimensiones_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var dimension = new DimensionTematica { Nombre = "Original", Activo = true };
        ctx.DimensionesTematicas.Add(dimension);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/dimensiones-tematicas/{dimension.Id}", new ModificarDimensionTematicaRequest("Modificada"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteDimensiones_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var dimension = new DimensionTematica { Nombre = "Para Baja", Activo = true };
        ctx.DimensionesTematicas.Add(dimension);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/dimensiones-tematicas/{dimension.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizada = await verificacion.DimensionesTematicas.SingleAsync(d => d.Id == dimension.Id);
        Assert.False(actualizada.Activo);
    }

    [Fact]
    public async Task GetDimensionesActivas_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Activa", Activo = true });
        ctx.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Inactiva", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/dimensiones-tematicas/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dimensiones = await response.Content.ReadFromJsonAsync<List<DimensionTematicaDto>>();
        Assert.Contains(dimensiones!, d => d.Nombre == "Activa");
        Assert.DoesNotContain(dimensiones!, d => d.Nombre == "Inactiva");
    }

    [Fact]
    public async Task GetDimensionesActivas_ConTokenOperador_Devuelve200()
    {
        // Gateada con GestionarTareas — ver nota completa en ZonasEndpointTests.
        await using var ctx = Factory.CrearContexto();
        ctx.DimensionesTematicas.Add(new DimensionTematica { Nombre = "Activa", Activo = true });
        await ctx.SaveChangesAsync();
        var (_, token) = await DatosDePrueba.SeedOperadorConTokenAsync(
            ctx, Factory.Services.GetRequiredService<IJwtTokenService>(), "operador.test");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/dimensiones-tematicas/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dimensiones = await response.Content.ReadFromJsonAsync<List<DimensionTematicaDto>>();
        Assert.Contains(dimensiones!, d => d.Nombre == "Activa");
    }
}
```

Crear `tests/StockApp.Api.Tests/OrganismosResponsablesEndpointTests.cs`:

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

public class OrganismosResponsablesEndpointTests : ApiTestBase
{
    public OrganismosResponsablesEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetOrganismos_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/organismos-responsables");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganismos_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/organismos-responsables");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOrganismos_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Intendencia de Colonia", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/organismos-responsables");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organismos = await response.Content.ReadFromJsonAsync<List<OrganismoResponsableDto>>();
        Assert.Contains(organismos!, o => o.Nombre == "Intendencia de Colonia");
    }

    [Fact]
    public async Task PostOrganismos_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/organismos-responsables", new CrearOrganismoResponsableRequest("Municipio de Carmelo"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.OrganismosResponsables.AnyAsync(o => o.Nombre == "Municipio de Carmelo"));
    }

    [Fact]
    public async Task PostOrganismos_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/organismos-responsables", new CrearOrganismoResponsableRequest("Municipio de Carmelo"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostOrganismos_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Intendencia de Colonia", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/organismos-responsables", new CrearOrganismoResponsableRequest("Intendencia de Colonia"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutOrganismos_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var organismo = new OrganismoResponsable { Nombre = "Original", Activo = true };
        ctx.OrganismosResponsables.Add(organismo);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/organismos-responsables/{organismo.Id}", new ModificarOrganismoResponsableRequest("Modificado"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteOrganismos_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var organismo = new OrganismoResponsable { Nombre = "Para Baja", Activo = true };
        ctx.OrganismosResponsables.Add(organismo);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/organismos-responsables/{organismo.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.OrganismosResponsables.SingleAsync(o => o.Id == organismo.Id);
        Assert.False(actualizado.Activo);
    }

    [Fact]
    public async Task GetOrganismosActivos_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Activo", Activo = true });
        ctx.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Inactivo", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/organismos-responsables/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organismos = await response.Content.ReadFromJsonAsync<List<OrganismoResponsableDto>>();
        Assert.Contains(organismos!, o => o.Nombre == "Activo");
        Assert.DoesNotContain(organismos!, o => o.Nombre == "Inactivo");
    }

    [Fact]
    public async Task GetOrganismosActivos_ConTokenOperador_Devuelve200()
    {
        // Gateada con GestionarTareas — ver nota completa en ZonasEndpointTests.
        await using var ctx = Factory.CrearContexto();
        ctx.OrganismosResponsables.Add(new OrganismoResponsable { Nombre = "Activo", Activo = true });
        await ctx.SaveChangesAsync();
        var (_, token) = await DatosDePrueba.SeedOperadorConTokenAsync(
            ctx, Factory.Services.GetRequiredService<IJwtTokenService>(), "operador.test");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/organismos-responsables/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var organismos = await response.Content.ReadFromJsonAsync<List<OrganismoResponsableDto>>();
        Assert.Contains(organismos!, o => o.Nombre == "Activo");
    }
}
```

Crear `tests/StockApp.Api.Tests/OrigenesFinanciamientoEndpointTests.cs`:

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

public class OrigenesFinanciamientoEndpointTests : ApiTestBase
{
    public OrigenesFinanciamientoEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetOrigenes_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/origenes-financiamiento");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetOrigenes_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/origenes-financiamiento");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOrigenes_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Presupuesto propio", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/origenes-financiamiento");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var origenes = await response.Content.ReadFromJsonAsync<List<OrigenFinanciamientoDto>>();
        Assert.Contains(origenes!, o => o.Nombre == "Presupuesto propio");
    }

    [Fact]
    public async Task PostOrigenes_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/origenes-financiamiento", new CrearOrigenFinanciamientoRequest("Fondo nacional"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.OrigenesFinanciamiento.AnyAsync(o => o.Nombre == "Fondo nacional"));
    }

    [Fact]
    public async Task PostOrigenes_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/origenes-financiamiento", new CrearOrigenFinanciamientoRequest("Fondo nacional"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostOrigenes_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Convenio", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/origenes-financiamiento", new CrearOrigenFinanciamientoRequest("Convenio"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutOrigenes_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var origen = new OrigenFinanciamiento { Nombre = "Original", Activo = true };
        ctx.OrigenesFinanciamiento.Add(origen);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/origenes-financiamiento/{origen.Id}", new ModificarOrigenFinanciamientoRequest("Modificado"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteOrigenes_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var origen = new OrigenFinanciamiento { Nombre = "Para Baja", Activo = true };
        ctx.OrigenesFinanciamiento.Add(origen);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/origenes-financiamiento/{origen.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.OrigenesFinanciamiento.SingleAsync(o => o.Id == origen.Id);
        Assert.False(actualizado.Activo);
    }

    [Fact]
    public async Task GetOrigenesActivos_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Activo", Activo = true });
        ctx.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Inactivo", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/origenes-financiamiento/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var origenes = await response.Content.ReadFromJsonAsync<List<OrigenFinanciamientoDto>>();
        Assert.Contains(origenes!, o => o.Nombre == "Activo");
        Assert.DoesNotContain(origenes!, o => o.Nombre == "Inactivo");
    }

    [Fact]
    public async Task GetOrigenesActivos_ConTokenOperador_Devuelve200()
    {
        // Gateada con GestionarTareas — ver nota completa en ZonasEndpointTests.
        await using var ctx = Factory.CrearContexto();
        ctx.OrigenesFinanciamiento.Add(new OrigenFinanciamiento { Nombre = "Activo", Activo = true });
        await ctx.SaveChangesAsync();
        var (_, token) = await DatosDePrueba.SeedOperadorConTokenAsync(
            ctx, Factory.Services.GetRequiredService<IJwtTokenService>(), "operador.test");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/origenes-financiamiento/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var origenes = await response.Content.ReadFromJsonAsync<List<OrigenFinanciamientoDto>>();
        Assert.Contains(origenes!, o => o.Nombre == "Activo");
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~ZonasEndpointTests|FullyQualifiedName~DimensionesTematicasEndpointTests|FullyQualifiedName~OrganismosResponsablesEndpointTests|FullyQualifiedName~OrigenesFinanciamientoEndpointTests"`
Expected: FALLA de compilación — `/zonas`, `/dimensiones-tematicas`, `/organismos-responsables`, `/origenes-financiamiento` no existen; `ZonaDto`/`CrearZonaRequest`/etc. tampoco.

- [ ] **Step 3: Escribir los cuatro endpoints**

Crear `src/StockApp.Api/Endpoints/ZonasEndpoints.cs`:

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearZonaRequest(string Nombre);
public record ModificarZonaRequest(string Nombre);
public record ZonaDto(int Id, string Nombre, bool Activo);

public static class ZonasEndpoints
{
    public static IEndpointRouteBuilder MapZonasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/zonas");

        group.MapGet("/", async (IZonaService zonas) =>
            Results.Ok((await zonas.ListarTodasAsync()).Select(AZonaDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearZonaRequest request, IZonaService zonas) =>
        {
            var id = await zonas.AltaAsync(new Zona { Nombre = request.Nombre });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarZonaRequest request, IZonaService zonas) =>
        {
            await zonas.ModificarAsync(new Zona { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, IZonaService zonas) =>
        {
            await zonas.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        // Gateada con GestionarTareas (permiso del consumidor), NO GestionarTablasMaestras
        // (permiso del administrador) — mismo patrón que GET /categorias/activas con
        // GestionarProductos. Ver decisión en Global Constraints del plan.
        group.MapGet("/activas", async (IZonaService zonas) =>
            Results.Ok((await zonas.ListarActivasAsync()).Select(AZonaDto)))
            .RequireAuthorization(Permisos.GestionarTareas);

        return app;
    }

    private static ZonaDto AZonaDto(Zona z) => new(z.Id, z.Nombre, z.Activo);
}
```

Crear `src/StockApp.Api/Endpoints/DimensionesTematicasEndpoints.cs`:

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearDimensionTematicaRequest(string Nombre);
public record ModificarDimensionTematicaRequest(string Nombre);
public record DimensionTematicaDto(int Id, string Nombre, bool Activo);

public static class DimensionesTematicasEndpoints
{
    public static IEndpointRouteBuilder MapDimensionesTematicasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dimensiones-tematicas");

        group.MapGet("/", async (IDimensionTematicaService dimensiones) =>
            Results.Ok((await dimensiones.ListarTodasAsync()).Select(ADimensionTematicaDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearDimensionTematicaRequest request, IDimensionTematicaService dimensiones) =>
        {
            var id = await dimensiones.AltaAsync(new DimensionTematica { Nombre = request.Nombre });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarDimensionTematicaRequest request, IDimensionTematicaService dimensiones) =>
        {
            await dimensiones.ModificarAsync(new DimensionTematica { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, IDimensionTematicaService dimensiones) =>
        {
            await dimensiones.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        // Gateada con GestionarTareas — ver nota en ZonasEndpoints.
        group.MapGet("/activas", async (IDimensionTematicaService dimensiones) =>
            Results.Ok((await dimensiones.ListarActivasAsync()).Select(ADimensionTematicaDto)))
            .RequireAuthorization(Permisos.GestionarTareas);

        return app;
    }

    private static DimensionTematicaDto ADimensionTematicaDto(DimensionTematica d) => new(d.Id, d.Nombre, d.Activo);
}
```

Crear `src/StockApp.Api/Endpoints/OrganismosResponsablesEndpoints.cs`:

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearOrganismoResponsableRequest(string Nombre);
public record ModificarOrganismoResponsableRequest(string Nombre);
public record OrganismoResponsableDto(int Id, string Nombre, bool Activo);

public static class OrganismosResponsablesEndpoints
{
    public static IEndpointRouteBuilder MapOrganismosResponsablesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/organismos-responsables");

        group.MapGet("/", async (IOrganismoResponsableService organismos) =>
            Results.Ok((await organismos.ListarTodasAsync()).Select(AOrganismoResponsableDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearOrganismoResponsableRequest request, IOrganismoResponsableService organismos) =>
        {
            var id = await organismos.AltaAsync(new OrganismoResponsable { Nombre = request.Nombre });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarOrganismoResponsableRequest request, IOrganismoResponsableService organismos) =>
        {
            await organismos.ModificarAsync(new OrganismoResponsable { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, IOrganismoResponsableService organismos) =>
        {
            await organismos.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        // Gateada con GestionarTareas — ver nota en ZonasEndpoints.
        group.MapGet("/activas", async (IOrganismoResponsableService organismos) =>
            Results.Ok((await organismos.ListarActivasAsync()).Select(AOrganismoResponsableDto)))
            .RequireAuthorization(Permisos.GestionarTareas);

        return app;
    }

    private static OrganismoResponsableDto AOrganismoResponsableDto(OrganismoResponsable o) => new(o.Id, o.Nombre, o.Activo);
}
```

Crear `src/StockApp.Api/Endpoints/OrigenesFinanciamientoEndpoints.cs`:

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearOrigenFinanciamientoRequest(string Nombre);
public record ModificarOrigenFinanciamientoRequest(string Nombre);
public record OrigenFinanciamientoDto(int Id, string Nombre, bool Activo);

public static class OrigenesFinanciamientoEndpoints
{
    public static IEndpointRouteBuilder MapOrigenesFinanciamientoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/origenes-financiamiento");

        group.MapGet("/", async (IOrigenFinanciamientoService origenes) =>
            Results.Ok((await origenes.ListarTodasAsync()).Select(AOrigenFinanciamientoDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearOrigenFinanciamientoRequest request, IOrigenFinanciamientoService origenes) =>
        {
            var id = await origenes.AltaAsync(new OrigenFinanciamiento { Nombre = request.Nombre });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarOrigenFinanciamientoRequest request, IOrigenFinanciamientoService origenes) =>
        {
            await origenes.ModificarAsync(new OrigenFinanciamiento { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, IOrigenFinanciamientoService origenes) =>
        {
            await origenes.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        // Gateada con GestionarTareas — ver nota en ZonasEndpoints.
        group.MapGet("/activas", async (IOrigenFinanciamientoService origenes) =>
            Results.Ok((await origenes.ListarActivasAsync()).Select(AOrigenFinanciamientoDto)))
            .RequireAuthorization(Permisos.GestionarTareas);

        return app;
    }

    private static OrigenFinanciamientoDto AOrigenFinanciamientoDto(OrigenFinanciamiento o) => new(o.Id, o.Nombre, o.Activo);
}
```

- [ ] **Step 4: Registrar DI y las rutas en `Program.cs`**

```csharp
// src/StockApp.Api/Program.cs
// Agregar DEBAJO del bloque "// Catálogo — tablas maestras (Fase 2b)" (después de la línea
// "// IUnidadMedidaRepository ya está registrado desde Fase 2a..."):

// Clasificadores de Tareas (spec 2026-09-08) — catálogos sin invalidación de cache (D14).
builder.Services.AddScoped<IZonaRepository, ZonaRepository>();
builder.Services.AddScoped<IZonaService, ZonaService>();
builder.Services.AddScoped<IDimensionTematicaRepository, DimensionTematicaRepository>();
builder.Services.AddScoped<IDimensionTematicaService, DimensionTematicaService>();
builder.Services.AddScoped<IOrganismoResponsableRepository, OrganismoResponsableRepository>();
builder.Services.AddScoped<IOrganismoResponsableService, OrganismoResponsableService>();
builder.Services.AddScoped<IOrigenFinanciamientoRepository, OrigenFinanciamientoRepository>();
builder.Services.AddScoped<IOrigenFinanciamientoService, OrigenFinanciamientoService>();
```

```csharp
// src/StockApp.Api/Program.cs
// Agregar DEBAJO de app.MapUnidadesMedidaEndpoints();:

app.MapZonasEndpoints();
app.MapDimensionesTematicasEndpoints();
app.MapOrganismosResponsablesEndpoints();
app.MapOrigenesFinanciamientoEndpoints();
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~ZonasEndpointTests|FullyQualifiedName~DimensionesTematicasEndpointTests|FullyQualifiedName~OrganismosResponsablesEndpointTests|FullyQualifiedName~OrigenesFinanciamientoEndpointTests"`
Expected: los 40 tests en verde (10 por catálogo × 4).

Run: `dotnet test tests/StockApp.Api.Tests`
Expected: todos los tests en verde, sin regresiones. (Recordar: NUNCA en paralelo con `StockApp.Application.Tests`.)

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api/Endpoints/ZonasEndpoints.cs \
        src/StockApp.Api/Endpoints/DimensionesTematicasEndpoints.cs \
        src/StockApp.Api/Endpoints/OrganismosResponsablesEndpoints.cs \
        src/StockApp.Api/Endpoints/OrigenesFinanciamientoEndpoints.cs \
        src/StockApp.Api/Program.cs \
        tests/StockApp.Api.Tests/ZonasEndpointTests.cs \
        tests/StockApp.Api.Tests/DimensionesTematicasEndpointTests.cs \
        tests/StockApp.Api.Tests/OrganismosResponsablesEndpointTests.cs \
        tests/StockApp.Api.Tests/OrigenesFinanciamientoEndpointTests.cs
git commit -m "feat(catalogo): agrega endpoints de los clasificadores de tareas"
```

---

### Task 6: ApiClient — los cuatro clientes HTTP

**Files:**
- Create: `src/StockApp.ApiClient/ZonaApiClient.cs`
- Create: `src/StockApp.ApiClient/DimensionTematicaApiClient.cs`
- Create: `src/StockApp.ApiClient/OrganismoResponsableApiClient.cs`
- Create: `src/StockApp.ApiClient/OrigenFinanciamientoApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/ZonaApiClientTests.cs`
- Test: `tests/StockApp.ApiClient.Tests/DimensionTematicaApiClientTests.cs`
- Test: `tests/StockApp.ApiClient.Tests/OrganismoResponsableApiClientTests.cs`
- Test: `tests/StockApp.ApiClient.Tests/OrigenFinanciamientoApiClientTests.cs`

**Interfaces:**
- Consumes: `IZonaService`/etc. (Task 4, hablan en entidades de dominio); rutas `/zonas`, `/dimensiones-tematicas`, `/organismos-responsables`, `/origenes-financiamiento` (Task 5, hablan en los DTO `ZonaDto`/etc.); `ApiErrores.EnviarAsync`/`AsegurarExitoAsync` (`src/StockApp.ApiClient/ApiErrores.cs`, sin cambios). Molde EXACTO de `CategoriaApiClient` (`src/StockApp.ApiClient/CategoriaApiClient.cs`).
- Produces (consumido por Task 8, registro DI en `App.axaml.cs`): `ZonaApiClient : IZonaService` y las tres clases análogas.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/StockApp.ApiClient.Tests/ZonaApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class ZonaApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETZonas_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Centro", activo = true },
            new { id = 2, nombre = "Norte", activo = false },
        }));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        var zonas = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/zonas", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, zonas.Count);
        Assert.Equal(1, zonas[0].Id);
        Assert.Equal("Centro", zonas[0].Nombre);
        Assert.False(zonas[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETZonasActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        var zonas = await client.ListarActivasAsync();

        Assert.Equal("/zonas/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(zonas);
    }

    [Fact]
    public async Task Alta_POSTZonas_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new Zona { Nombre = "Centro" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/zonas", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Centro\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new Zona { Id = 3, Nombre = "Norte Ampliado" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/zonas/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Norte Ampliado\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEZonasId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/zonas/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe una zona con el nombre 'Centro'."));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new Zona { Nombre = "Centro" }));

        Assert.Equal("Ya existe una zona con el nombre 'Centro'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Zona 99 no encontrada."));
        var client = new ZonaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
```

Crear `tests/StockApp.ApiClient.Tests/DimensionTematicaApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class DimensionTematicaApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETDimensionesTematicas_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Infraestructura", activo = true },
            new { id = 2, nombre = "Tránsito", activo = false },
        }));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        var dimensiones = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/dimensiones-tematicas", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, dimensiones.Count);
        Assert.Equal(1, dimensiones[0].Id);
        Assert.Equal("Infraestructura", dimensiones[0].Nombre);
        Assert.False(dimensiones[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETDimensionesTematicasActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        var dimensiones = await client.ListarActivasAsync();

        Assert.Equal("/dimensiones-tematicas/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(dimensiones);
    }

    [Fact]
    public async Task Alta_POSTDimensionesTematicas_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new DimensionTematica { Nombre = "Infraestructura" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/dimensiones-tematicas", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Infraestructura\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new DimensionTematica { Id = 3, Nombre = "Tránsito y Movilidad" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/dimensiones-tematicas/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Tránsito y Movilidad\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEDimensionesTematicasId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/dimensiones-tematicas/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe una dimensión con el nombre 'Infraestructura'."));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new DimensionTematica { Nombre = "Infraestructura" }));

        Assert.Equal("Ya existe una dimensión con el nombre 'Infraestructura'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Dimensión 99 no encontrada."));
        var client = new DimensionTematicaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
```

Crear `tests/StockApp.ApiClient.Tests/OrganismoResponsableApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class OrganismoResponsableApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETOrganismosResponsables_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Intendencia de Colonia", activo = true },
            new { id = 2, nombre = "Municipio de Carmelo", activo = false },
        }));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        var organismos = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/organismos-responsables", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, organismos.Count);
        Assert.Equal(1, organismos[0].Id);
        Assert.Equal("Intendencia de Colonia", organismos[0].Nombre);
        Assert.False(organismos[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETOrganismosResponsablesActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        var organismos = await client.ListarActivasAsync();

        Assert.Equal("/organismos-responsables/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(organismos);
    }

    [Fact]
    public async Task Alta_POSTOrganismosResponsables_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new OrganismoResponsable { Nombre = "Intendencia de Colonia" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/organismos-responsables", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Intendencia de Colonia\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new OrganismoResponsable { Id = 3, Nombre = "Municipio de Carmelo (Junta Local)" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/organismos-responsables/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Municipio de Carmelo (Junta Local)\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEOrganismosResponsablesId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/organismos-responsables/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe un organismo con el nombre 'Intendencia de Colonia'."));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new OrganismoResponsable { Nombre = "Intendencia de Colonia" }));

        Assert.Equal("Ya existe un organismo con el nombre 'Intendencia de Colonia'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Organismo 99 no encontrado."));
        var client = new OrganismoResponsableApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
```

Crear `tests/StockApp.ApiClient.Tests/OrigenFinanciamientoApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class OrigenFinanciamientoApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETOrigenesFinanciamiento_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Presupuesto propio", activo = true },
            new { id = 2, nombre = "Fondo nacional", activo = false },
        }));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var origenes = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/origenes-financiamiento", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, origenes.Count);
        Assert.Equal(1, origenes[0].Id);
        Assert.Equal("Presupuesto propio", origenes[0].Nombre);
        Assert.False(origenes[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETOrigenesFinanciamientoActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var origenes = await client.ListarActivasAsync();

        Assert.Equal("/origenes-financiamiento/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(origenes);
    }

    [Fact]
    public async Task Alta_POSTOrigenesFinanciamiento_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new OrigenFinanciamiento { Nombre = "Presupuesto propio" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/origenes-financiamiento", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Presupuesto propio\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new OrigenFinanciamiento { Id = 3, Nombre = "Fondo nacional de infraestructura" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/origenes-financiamiento/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Fondo nacional de infraestructura\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEOrigenesFinanciamientoId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/origenes-financiamiento/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe un origen de financiamiento con el nombre 'Presupuesto propio'."));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new OrigenFinanciamiento { Nombre = "Presupuesto propio" }));

        Assert.Equal("Ya existe un origen de financiamiento con el nombre 'Presupuesto propio'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Origen de financiamiento 99 no encontrado."));
        var client = new OrigenFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~ZonaApiClientTests|FullyQualifiedName~DimensionTematicaApiClientTests|FullyQualifiedName~OrganismoResponsableApiClientTests|FullyQualifiedName~OrigenFinanciamientoApiClientTests"`
Expected: FALLA de compilación — `ZonaApiClient`, `DimensionTematicaApiClient`, `OrganismoResponsableApiClient`, `OrigenFinanciamientoApiClient` no existen.

- [ ] **Step 3: Implementación de los cuatro clientes**

Crear `src/StockApp.ApiClient/ZonaApiClient.cs`:

```csharp
using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record ZonaWire(int Id, string Nombre, bool Activo);
internal sealed record ZonaBody(string Nombre);

public sealed class ZonaApiClient : IZonaService
{
    private readonly HttpClient _http;

    public ZonaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(Zona zona)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("zonas", new ZonaBody(zona.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la zona.");
        return creado.Id;
    }

    public async Task ModificarAsync(Zona zona)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"zonas/{zona.Id}", new ZonaBody(zona.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"zonas/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<Zona>> ListarTodasAsync() => ListarAsync("zonas");

    public Task<IReadOnlyList<Zona>> ListarActivasAsync() => ListarAsync("zonas/activas");

    private async Task<IReadOnlyList<Zona>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<ZonaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static Zona AEntidad(ZonaWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
```

Crear `src/StockApp.ApiClient/DimensionTematicaApiClient.cs`:

```csharp
using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record DimensionTematicaWire(int Id, string Nombre, bool Activo);
internal sealed record DimensionTematicaBody(string Nombre);

public sealed class DimensionTematicaApiClient : IDimensionTematicaService
{
    private readonly HttpClient _http;

    public DimensionTematicaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(DimensionTematica dimension)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("dimensiones-tematicas", new DimensionTematicaBody(dimension.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la dimensión.");
        return creado.Id;
    }

    public async Task ModificarAsync(DimensionTematica dimension)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"dimensiones-tematicas/{dimension.Id}", new DimensionTematicaBody(dimension.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"dimensiones-tematicas/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync() => ListarAsync("dimensiones-tematicas");

    public Task<IReadOnlyList<DimensionTematica>> ListarActivasAsync() => ListarAsync("dimensiones-tematicas/activas");

    private async Task<IReadOnlyList<DimensionTematica>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<DimensionTematicaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static DimensionTematica AEntidad(DimensionTematicaWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
```

Crear `src/StockApp.ApiClient/OrganismoResponsableApiClient.cs`:

```csharp
using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record OrganismoResponsableWire(int Id, string Nombre, bool Activo);
internal sealed record OrganismoResponsableBody(string Nombre);

public sealed class OrganismoResponsableApiClient : IOrganismoResponsableService
{
    private readonly HttpClient _http;

    public OrganismoResponsableApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(OrganismoResponsable organismo)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("organismos-responsables", new OrganismoResponsableBody(organismo.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el organismo.");
        return creado.Id;
    }

    public async Task ModificarAsync(OrganismoResponsable organismo)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"organismos-responsables/{organismo.Id}", new OrganismoResponsableBody(organismo.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"organismos-responsables/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync() => ListarAsync("organismos-responsables");

    public Task<IReadOnlyList<OrganismoResponsable>> ListarActivasAsync() => ListarAsync("organismos-responsables/activas");

    private async Task<IReadOnlyList<OrganismoResponsable>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<OrganismoResponsableWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static OrganismoResponsable AEntidad(OrganismoResponsableWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
```

Crear `src/StockApp.ApiClient/OrigenFinanciamientoApiClient.cs`:

```csharp
using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record OrigenFinanciamientoWire(int Id, string Nombre, bool Activo);
internal sealed record OrigenFinanciamientoBody(string Nombre);

public sealed class OrigenFinanciamientoApiClient : IOrigenFinanciamientoService
{
    private readonly HttpClient _http;

    public OrigenFinanciamientoApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(OrigenFinanciamiento origen)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("origenes-financiamiento", new OrigenFinanciamientoBody(origen.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el origen de financiamiento.");
        return creado.Id;
    }

    public async Task ModificarAsync(OrigenFinanciamiento origen)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"origenes-financiamiento/{origen.Id}", new OrigenFinanciamientoBody(origen.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"origenes-financiamiento/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync() => ListarAsync("origenes-financiamiento");

    public Task<IReadOnlyList<OrigenFinanciamiento>> ListarActivasAsync() => ListarAsync("origenes-financiamiento/activas");

    private async Task<IReadOnlyList<OrigenFinanciamiento>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<OrigenFinanciamientoWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static OrigenFinanciamiento AEntidad(OrigenFinanciamientoWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~ZonaApiClientTests|FullyQualifiedName~DimensionTematicaApiClientTests|FullyQualifiedName~OrganismoResponsableApiClientTests|FullyQualifiedName~OrigenFinanciamientoApiClientTests"`
Expected: los 28 tests en verde (7 por catálogo × 4).

Run: `dotnet test tests/StockApp.ApiClient.Tests`
Expected: todos los tests en verde, sin regresiones.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.ApiClient/ZonaApiClient.cs \
        src/StockApp.ApiClient/DimensionTematicaApiClient.cs \
        src/StockApp.ApiClient/OrganismoResponsableApiClient.cs \
        src/StockApp.ApiClient/OrigenFinanciamientoApiClient.cs \
        tests/StockApp.ApiClient.Tests/ZonaApiClientTests.cs \
        tests/StockApp.ApiClient.Tests/DimensionTematicaApiClientTests.cs \
        tests/StockApp.ApiClient.Tests/OrganismoResponsableApiClientTests.cs \
        tests/StockApp.ApiClient.Tests/OrigenFinanciamientoApiClientTests.cs
git commit -m "feat(catalogo): agrega ApiClients de los clasificadores de tareas"
```

---

### Task 7: Presentation — ViewModels de listado y formulario de los cuatro catálogos

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Catalogo/ZonaListViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Catalogo/ZonaFormViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Catalogo/DimensionTematicaListViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Catalogo/DimensionTematicaFormViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Catalogo/OrganismoResponsableListViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Catalogo/OrganismoResponsableFormViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Catalogo/OrigenFinanciamientoListViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Catalogo/OrigenFinanciamientoFormViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ZonaViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/DimensionTematicaViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/OrganismoResponsableViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/OrigenFinanciamientoViewModelTests.cs`

**Interfaces:**
- Consumes: `IZonaService`/etc. (Task 4/6, resuelto por DI a `ZonaApiClient`/etc. en runtime, mockeado en tests); `INavigationService`/`IConfirmacionService` (`src/StockApp.Presentation/Navigation/`, `src/StockApp.Presentation/Services/`, sin cambios). Molde EXACTO de `CategoriaListViewModel`/`CategoriaFormViewModel` (`src/StockApp.Presentation/ViewModels/Catalogo/`).
- Produces (consumido por Task 8): `ZonaListViewModel` (`Items`, `ItemSeleccionado`, `CargarAsync()`, `NuevoCommand`, `EditarCommand`, `BajaCommand`) + `ZonaFormViewModel` (`Nombre`, `Titulo`, `EsEdicion`, `MensajeError`, `CargarParaEditar`, `GuardarCommand`) y las tres parejas análogas.

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ZonaViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class ZonaListViewModelTests
{
    private static (ZonaListViewModel vm, Mock<IZonaService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<Zona>? zonas = null)
    {
        var svcMock = new Mock<IZonaService>();
        svcMock
            .Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(zonas ?? new List<Zona>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new ZonaListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var zonas = new List<Zona>
        {
            new() { Id = 1, Nombre = "Centro" },
            new() { Id = 2, Nombre = "Norte" }
        };
        var (vm, svcMock, _, _) = Crear(zonas);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Centro", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svcMock = new Mock<IZonaService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        var vm = new ZonaListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAZonaFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ZonaFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaAlFormularioEnModoEdicion()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, navMock, _) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        await vm.EditarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ZonaFormViewModel>(
            It.IsAny<System.Action<ZonaFormViewModel>>()), Times.Once);
    }

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditarCommand_ItemInactivo_EstaDeshabilitado()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaReglaDeNegocio_NoPropagaYInforma()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        var mensaje = "La zona 5 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaEntidadNoEncontrada_NoPropagaYInforma()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        var mensaje = "Zona 5 no encontrada.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new EntidadNoEncontradaException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public void BajaCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemInactivo_EstaDeshabilitado()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var zona = new Zona { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, _, _) = Crear(new List<Zona> { zona });
        await vm.CargarAsync();
        vm.ItemSeleccionado = zona;

        Assert.True(vm.BajaCommand.CanExecute(null));
    }
}

public class ZonaFormViewModelTests
{
    private static (ZonaFormViewModel vm, Mock<IZonaService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IZonaService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<Zona>()))
            .ReturnsAsync(1);
        svcMock
            .Setup(s => s.ModificarAsync(It.IsAny<Zona>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new ZonaFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConNombre_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Centro";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<Zona>(z => z.Nombre == "Centro")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Centro";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<ZonaListViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_LlamaModificarConElId()
    {
        var (vm, svcMock, _) = Crear();
        vm.CargarParaEditar(new Zona { Id = 3, Nombre = "Sur", Activo = true });
        vm.Nombre = "Sur (renombrada)";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<Zona>(z => z.Id == 3 && z.Nombre == "Sur (renombrada)")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_ReglaDeNegocio_MuestraMensajeSinNavegar()
    {
        var (vm, svcMock, navMock) = Crear();
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<Zona>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe una zona con el nombre 'Sur'."));
        vm.CargarParaEditar(new Zona { Id = 3, Nombre = "Sur", Activo = true });
        vm.Nombre = "Sur";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe una zona con el nombre 'Sur'.", vm.MensajeError);
        navMock.Verify(n => n.Navegar<ZonaListViewModel>(), Times.Never);
    }
}
```

Crear `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/DimensionTematicaViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class DimensionTematicaListViewModelTests
{
    private static (DimensionTematicaListViewModel vm, Mock<IDimensionTematicaService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<DimensionTematica>? dimensiones = null)
    {
        var svcMock = new Mock<IDimensionTematicaService>();
        svcMock
            .Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(dimensiones ?? new List<DimensionTematica>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new DimensionTematicaListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var dimensiones = new List<DimensionTematica>
        {
            new() { Id = 1, Nombre = "Infraestructura" },
            new() { Id = 2, Nombre = "Tránsito" }
        };
        var (vm, svcMock, _, _) = Crear(dimensiones);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Infraestructura", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svcMock = new Mock<IDimensionTematicaService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        var vm = new DimensionTematicaListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task NuevoCommand_NavegaADimensionTematicaFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<DimensionTematicaFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaAlFormularioEnModoEdicion()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, navMock, _) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        await vm.EditarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<DimensionTematicaFormViewModel>(
            It.IsAny<System.Action<DimensionTematicaFormViewModel>>()), Times.Once);
    }

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditarCommand_ItemInactivo_EstaDeshabilitado()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaReglaDeNegocio_NoPropagaYInforma()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        var mensaje = "La dimensión 5 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaEntidadNoEncontrada_NoPropagaYInforma()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        var mensaje = "Dimensión 5 no encontrada.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new EntidadNoEncontradaException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public void BajaCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemInactivo_EstaDeshabilitado()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var dimension = new DimensionTematica { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, _, _) = Crear(new List<DimensionTematica> { dimension });
        await vm.CargarAsync();
        vm.ItemSeleccionado = dimension;

        Assert.True(vm.BajaCommand.CanExecute(null));
    }
}

public class DimensionTematicaFormViewModelTests
{
    private static (DimensionTematicaFormViewModel vm, Mock<IDimensionTematicaService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IDimensionTematicaService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<DimensionTematica>()))
            .ReturnsAsync(1);
        svcMock
            .Setup(s => s.ModificarAsync(It.IsAny<DimensionTematica>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new DimensionTematicaFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConNombre_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Infraestructura";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<DimensionTematica>(d => d.Nombre == "Infraestructura")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Infraestructura";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<DimensionTematicaListViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_LlamaModificarConElId()
    {
        var (vm, svcMock, _) = Crear();
        vm.CargarParaEditar(new DimensionTematica { Id = 3, Nombre = "Tránsito", Activo = true });
        vm.Nombre = "Tránsito (renombrada)";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<DimensionTematica>(d => d.Id == 3 && d.Nombre == "Tránsito (renombrada)")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_ReglaDeNegocio_MuestraMensajeSinNavegar()
    {
        var (vm, svcMock, navMock) = Crear();
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<DimensionTematica>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe una dimensión con el nombre 'Tránsito'."));
        vm.CargarParaEditar(new DimensionTematica { Id = 3, Nombre = "Tránsito", Activo = true });
        vm.Nombre = "Tránsito";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe una dimensión con el nombre 'Tránsito'.", vm.MensajeError);
        navMock.Verify(n => n.Navegar<DimensionTematicaListViewModel>(), Times.Never);
    }
}
```

Crear `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/OrganismoResponsableViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class OrganismoResponsableListViewModelTests
{
    private static (OrganismoResponsableListViewModel vm, Mock<IOrganismoResponsableService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<OrganismoResponsable>? organismos = null)
    {
        var svcMock = new Mock<IOrganismoResponsableService>();
        svcMock
            .Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(organismos ?? new List<OrganismoResponsable>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new OrganismoResponsableListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var organismos = new List<OrganismoResponsable>
        {
            new() { Id = 1, Nombre = "Intendencia de Colonia" },
            new() { Id = 2, Nombre = "Municipio de Carmelo" }
        };
        var (vm, svcMock, _, _) = Crear(organismos);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Intendencia de Colonia", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svcMock = new Mock<IOrganismoResponsableService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        var vm = new OrganismoResponsableListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAOrganismoResponsableFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<OrganismoResponsableFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaAlFormularioEnModoEdicion()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, navMock, _) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        await vm.EditarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<OrganismoResponsableFormViewModel>(
            It.IsAny<System.Action<OrganismoResponsableFormViewModel>>()), Times.Once);
    }

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditarCommand_ItemInactivo_EstaDeshabilitado()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaReglaDeNegocio_NoPropagaYInforma()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        var mensaje = "El organismo 5 ya está inactivo.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaEntidadNoEncontrada_NoPropagaYInforma()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        var mensaje = "Organismo 5 no encontrado.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new EntidadNoEncontradaException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public void BajaCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemInactivo_EstaDeshabilitado()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var organismo = new OrganismoResponsable { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, _, _) = Crear(new List<OrganismoResponsable> { organismo });
        await vm.CargarAsync();
        vm.ItemSeleccionado = organismo;

        Assert.True(vm.BajaCommand.CanExecute(null));
    }
}

public class OrganismoResponsableFormViewModelTests
{
    private static (OrganismoResponsableFormViewModel vm, Mock<IOrganismoResponsableService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IOrganismoResponsableService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<OrganismoResponsable>()))
            .ReturnsAsync(1);
        svcMock
            .Setup(s => s.ModificarAsync(It.IsAny<OrganismoResponsable>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new OrganismoResponsableFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConNombre_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Intendencia de Colonia";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<OrganismoResponsable>(o => o.Nombre == "Intendencia de Colonia")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Intendencia de Colonia";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<OrganismoResponsableListViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_LlamaModificarConElId()
    {
        var (vm, svcMock, _) = Crear();
        vm.CargarParaEditar(new OrganismoResponsable { Id = 3, Nombre = "Municipio de Carmelo", Activo = true });
        vm.Nombre = "Municipio de Carmelo (renombrado)";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<OrganismoResponsable>(o => o.Id == 3 && o.Nombre == "Municipio de Carmelo (renombrado)")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_ReglaDeNegocio_MuestraMensajeSinNavegar()
    {
        var (vm, svcMock, navMock) = Crear();
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<OrganismoResponsable>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe un organismo con el nombre 'Municipio de Carmelo'."));
        vm.CargarParaEditar(new OrganismoResponsable { Id = 3, Nombre = "Municipio de Carmelo", Activo = true });
        vm.Nombre = "Municipio de Carmelo";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe un organismo con el nombre 'Municipio de Carmelo'.", vm.MensajeError);
        navMock.Verify(n => n.Navegar<OrganismoResponsableListViewModel>(), Times.Never);
    }
}
```

Crear `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/OrigenFinanciamientoViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class OrigenFinanciamientoListViewModelTests
{
    private static (OrigenFinanciamientoListViewModel vm, Mock<IOrigenFinanciamientoService> svcMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<OrigenFinanciamiento>? origenes = null)
    {
        var svcMock = new Mock<IOrigenFinanciamientoService>();
        svcMock
            .Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(origenes ?? new List<OrigenFinanciamiento>());
        svcMock
            .Setup(s => s.BajaLogicaAsync(It.IsAny<int>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new OrigenFinanciamientoListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var origenes = new List<OrigenFinanciamiento>
        {
            new() { Id = 1, Nombre = "Presupuesto propio" },
            new() { Id = 2, Nombre = "Fondo nacional" }
        };
        var (vm, svcMock, _, _) = Crear(origenes);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Presupuesto propio", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svcMock = new Mock<IOrigenFinanciamientoService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ThrowsAsync(new UnauthorizedAccessException());
        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        var vm = new OrigenFinanciamientoListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAOrigenFinanciamientoFormViewModel()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<OrigenFinanciamientoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaAlFormularioEnModoEdicion()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, navMock, _) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        await vm.EditarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<OrigenFinanciamientoFormViewModel>(
            It.IsAny<System.Action<OrigenFinanciamientoFormViewModel>>()), Times.Once);
    }

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task EditarCommand_ItemInactivo_EstaDeshabilitado()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ConItemSeleccionado_PideConfirmacionYLlamaServicio()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_SiNoConfirma_NoLlamaServicio()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.BajaCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaReglaDeNegocio_NoPropagaYInforma()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        var mensaje = "El origen de financiamiento 5 ya está inactivo.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ServicioLanzaEntidadNoEncontrada_NoPropagaYInforma()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        var mensaje = "Origen de financiamiento 5 no encontrado.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new EntidadNoEncontradaException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public void BajaCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemInactivo_EstaDeshabilitado()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = false };
        var (vm, _, _, _) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ItemActivo_EstaHabilitado()
    {
        var origen = new OrigenFinanciamiento { Id = 5, Nombre = "Prueba", Activo = true };
        var (vm, _, _, _) = Crear(new List<OrigenFinanciamiento> { origen });
        await vm.CargarAsync();
        vm.ItemSeleccionado = origen;

        Assert.True(vm.BajaCommand.CanExecute(null));
    }
}

public class OrigenFinanciamientoFormViewModelTests
{
    private static (OrigenFinanciamientoFormViewModel vm, Mock<IOrigenFinanciamientoService> svcMock, Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IOrigenFinanciamientoService>();
        svcMock
            .Setup(s => s.AltaAsync(It.IsAny<OrigenFinanciamiento>()))
            .ReturnsAsync(1);
        svcMock
            .Setup(s => s.ModificarAsync(It.IsAny<OrigenFinanciamiento>()))
            .Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new OrigenFinanciamientoFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConNombre_LlamaAltaAsync()
    {
        var (vm, svcMock, _) = Crear();
        vm.Nombre = "Presupuesto propio";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<OrigenFinanciamiento>(o => o.Nombre == "Presupuesto propio")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_Exitoso_NavegaAListado()
    {
        var (vm, _, navMock) = Crear();
        vm.Nombre = "Presupuesto propio";

        await vm.GuardarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<OrigenFinanciamientoListViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_LlamaModificarConElId()
    {
        var (vm, svcMock, _) = Crear();
        vm.CargarParaEditar(new OrigenFinanciamiento { Id = 3, Nombre = "Fondo nacional", Activo = true });
        vm.Nombre = "Fondo nacional (renombrado)";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<OrigenFinanciamiento>(o => o.Id == 3 && o.Nombre == "Fondo nacional (renombrado)")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_ReglaDeNegocio_MuestraMensajeSinNavegar()
    {
        var (vm, svcMock, navMock) = Crear();
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<OrigenFinanciamiento>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe un origen de financiamiento con el nombre 'Fondo nacional'."));
        vm.CargarParaEditar(new OrigenFinanciamiento { Id = 3, Nombre = "Fondo nacional", Activo = true });
        vm.Nombre = "Fondo nacional";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe un origen de financiamiento con el nombre 'Fondo nacional'.", vm.MensajeError);
        navMock.Verify(n => n.Navegar<OrigenFinanciamientoListViewModel>(), Times.Never);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ZonaViewModelTests|FullyQualifiedName~DimensionTematicaViewModelTests|FullyQualifiedName~OrganismoResponsableViewModelTests|FullyQualifiedName~OrigenFinanciamientoViewModelTests"`
Expected: FALLA de compilación — `ZonaListViewModel`, `ZonaFormViewModel` y las tres parejas análogas no existen.

- [ ] **Step 3: Implementación de los ocho ViewModels**

Crear `src/StockApp.Presentation/ViewModels/Catalogo/ZonaListViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de zonas con alta y baja lógica. Solo accesible para Admin
/// (el menú filtra por permiso; el servicio Application revalida en cada operación).
/// </summary>
public partial class ZonaListViewModel : ViewModelBase
{
    private readonly IZonaService         _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private Zona? _itemSeleccionado;

    public ObservableCollection<Zona> Items { get; } = new();

    public ZonaListViewModel(
        IZonaService service,
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
            var resultados = await _service.ListarTodasAsync();
            Items.Clear();
            foreach (var z in resultados)
                Items.Add(z);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<ZonaFormViewModel>());

    private bool PuedeDarBaja()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    private bool PuedeEditar() => PuedeDarBaja();

    [RelayCommand(CanExecute = nameof(PuedeEditar))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionada = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<ZonaFormViewModel>(vm => vm.CargarParaEditar(seleccionada)));
    }

    [RelayCommand(CanExecute = nameof(PuedeDarBaja))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja la zona \"{ItemSeleccionado.Nombre}\"?");
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

Crear `src/StockApp.Presentation/ViewModels/Catalogo/ZonaFormViewModel.cs`:

```csharp
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de una zona.
/// </summary>
public partial class ZonaFormViewModel : ViewModelBase
{
    private readonly IZonaService       _service;
    private readonly INavigationService _navigation;

    private int _idEdicion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar zona" : "Nueva zona";

    public ZonaFormViewModel(IZonaService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    /// <summary>Precarga el formulario en modo edición (llamado por el overload de Navegar).</summary>
    public void CargarParaEditar(Zona zona)
    {
        _idEdicion = zona.Id;
        Nombre     = zona.Nombre;
        EsEdicion  = true;
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Nombre);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(new Zona { Id = _idEdicion, Nombre = Nombre });
            else
                await _service.AltaAsync(new Zona { Nombre = Nombre });

            _navigation.Navegar<ZonaListViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
```

Crear `src/StockApp.Presentation/ViewModels/Catalogo/DimensionTematicaListViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de dimensiones temáticas con alta y baja lógica. Solo accesible para Admin.
/// </summary>
public partial class DimensionTematicaListViewModel : ViewModelBase
{
    private readonly IDimensionTematicaService _service;
    private readonly INavigationService        _navigation;
    private readonly IConfirmacionService      _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private DimensionTematica? _itemSeleccionado;

    public ObservableCollection<DimensionTematica> Items { get; } = new();

    public DimensionTematicaListViewModel(
        IDimensionTematicaService service,
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
            var resultados = await _service.ListarTodasAsync();
            Items.Clear();
            foreach (var d in resultados)
                Items.Add(d);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<DimensionTematicaFormViewModel>());

    private bool PuedeDarBaja()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    private bool PuedeEditar() => PuedeDarBaja();

    [RelayCommand(CanExecute = nameof(PuedeEditar))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionada = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<DimensionTematicaFormViewModel>(vm => vm.CargarParaEditar(seleccionada)));
    }

    [RelayCommand(CanExecute = nameof(PuedeDarBaja))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja la dimensión \"{ItemSeleccionado.Nombre}\"?");
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

Crear `src/StockApp.Presentation/ViewModels/Catalogo/DimensionTematicaFormViewModel.cs`:

```csharp
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de una dimensión temática.
/// </summary>
public partial class DimensionTematicaFormViewModel : ViewModelBase
{
    private readonly IDimensionTematicaService _service;
    private readonly INavigationService        _navigation;

    private int _idEdicion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar dimensión" : "Nueva dimensión";

    public DimensionTematicaFormViewModel(IDimensionTematicaService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public void CargarParaEditar(DimensionTematica dimension)
    {
        _idEdicion = dimension.Id;
        Nombre     = dimension.Nombre;
        EsEdicion  = true;
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Nombre);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(new DimensionTematica { Id = _idEdicion, Nombre = Nombre });
            else
                await _service.AltaAsync(new DimensionTematica { Nombre = Nombre });

            _navigation.Navegar<DimensionTematicaListViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
```

Crear `src/StockApp.Presentation/ViewModels/Catalogo/OrganismoResponsableListViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de organismos responsables con alta y baja lógica. Solo accesible para Admin.
/// </summary>
public partial class OrganismoResponsableListViewModel : ViewModelBase
{
    private readonly IOrganismoResponsableService _service;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private OrganismoResponsable? _itemSeleccionado;

    public ObservableCollection<OrganismoResponsable> Items { get; } = new();

    public OrganismoResponsableListViewModel(
        IOrganismoResponsableService service,
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
            var resultados = await _service.ListarTodasAsync();
            Items.Clear();
            foreach (var o in resultados)
                Items.Add(o);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<OrganismoResponsableFormViewModel>());

    private bool PuedeDarBaja()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    private bool PuedeEditar() => PuedeDarBaja();

    [RelayCommand(CanExecute = nameof(PuedeEditar))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionada = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<OrganismoResponsableFormViewModel>(vm => vm.CargarParaEditar(seleccionada)));
    }

    [RelayCommand(CanExecute = nameof(PuedeDarBaja))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja el organismo \"{ItemSeleccionado.Nombre}\"?");
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

Crear `src/StockApp.Presentation/ViewModels/Catalogo/OrganismoResponsableFormViewModel.cs`:

```csharp
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de un organismo responsable.
/// </summary>
public partial class OrganismoResponsableFormViewModel : ViewModelBase
{
    private readonly IOrganismoResponsableService _service;
    private readonly INavigationService           _navigation;

    private int _idEdicion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar organismo responsable" : "Nuevo organismo responsable";

    public OrganismoResponsableFormViewModel(IOrganismoResponsableService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public void CargarParaEditar(OrganismoResponsable organismo)
    {
        _idEdicion = organismo.Id;
        Nombre     = organismo.Nombre;
        EsEdicion  = true;
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Nombre);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(new OrganismoResponsable { Id = _idEdicion, Nombre = Nombre });
            else
                await _service.AltaAsync(new OrganismoResponsable { Nombre = Nombre });

            _navigation.Navegar<OrganismoResponsableListViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
```

Crear `src/StockApp.Presentation/ViewModels/Catalogo/OrigenFinanciamientoListViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Listado de orígenes de financiamiento con alta y baja lógica. Solo accesible para Admin.
/// </summary>
public partial class OrigenFinanciamientoListViewModel : ViewModelBase
{
    private readonly IOrigenFinanciamientoService _service;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private OrigenFinanciamiento? _itemSeleccionado;

    public ObservableCollection<OrigenFinanciamiento> Items { get; } = new();

    public OrigenFinanciamientoListViewModel(
        IOrigenFinanciamientoService service,
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
            var resultados = await _service.ListarTodasAsync();
            Items.Clear();
            foreach (var o in resultados)
                Items.Add(o);
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<OrigenFinanciamientoFormViewModel>());

    private bool PuedeDarBaja()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    private bool PuedeEditar() => PuedeDarBaja();

    [RelayCommand(CanExecute = nameof(PuedeEditar))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionada = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<OrigenFinanciamientoFormViewModel>(vm => vm.CargarParaEditar(seleccionada)));
    }

    [RelayCommand(CanExecute = nameof(PuedeDarBaja))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja el origen de financiamiento \"{ItemSeleccionado.Nombre}\"?");
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

Crear `src/StockApp.Presentation/ViewModels/Catalogo/OrigenFinanciamientoFormViewModel.cs`:

```csharp
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Formulario de alta / edición de un origen de financiamiento.
/// </summary>
public partial class OrigenFinanciamientoFormViewModel : ViewModelBase
{
    private readonly IOrigenFinanciamientoService _service;
    private readonly INavigationService           _navigation;

    private int _idEdicion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar origen de financiamiento" : "Nuevo origen de financiamiento";

    public OrigenFinanciamientoFormViewModel(IOrigenFinanciamientoService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public void CargarParaEditar(OrigenFinanciamiento origen)
    {
        _idEdicion = origen.Id;
        Nombre     = origen.Nombre;
        EsEdicion  = true;
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Nombre);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(new OrigenFinanciamiento { Id = _idEdicion, Nombre = Nombre });
            else
                await _service.AltaAsync(new OrigenFinanciamiento { Nombre = Nombre });

            _navigation.Navegar<OrigenFinanciamientoListViewModel>();
        }
        catch (System.Exception ex)
        {
            MensajeError = ex.Message;
        }
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ZonaViewModelTests|FullyQualifiedName~DimensionTematicaViewModelTests|FullyQualifiedName~OrganismoResponsableViewModelTests|FullyQualifiedName~OrigenFinanciamientoViewModelTests"`
Expected: los 72 tests en verde (18 por catálogo × 4: 13 de List + 5 de Form).

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: todos los tests en verde, sin regresiones.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Catalogo/ZonaListViewModel.cs \
        src/StockApp.Presentation/ViewModels/Catalogo/ZonaFormViewModel.cs \
        src/StockApp.Presentation/ViewModels/Catalogo/DimensionTematicaListViewModel.cs \
        src/StockApp.Presentation/ViewModels/Catalogo/DimensionTematicaFormViewModel.cs \
        src/StockApp.Presentation/ViewModels/Catalogo/OrganismoResponsableListViewModel.cs \
        src/StockApp.Presentation/ViewModels/Catalogo/OrganismoResponsableFormViewModel.cs \
        src/StockApp.Presentation/ViewModels/Catalogo/OrigenFinanciamientoListViewModel.cs \
        src/StockApp.Presentation/ViewModels/Catalogo/OrigenFinanciamientoFormViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ZonaViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Catalogo/DimensionTematicaViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Catalogo/OrganismoResponsableViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Catalogo/OrigenFinanciamientoViewModelTests.cs
git commit -m "feat(catalogo): agrega ViewModels de los clasificadores de tareas"
```

---

### Task 8: Presentation — Vistas axaml de los cuatro catálogos + registro DI

**Files:**
- Create: `src/StockApp.Presentation/Views/Catalogo/ZonaListView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Catalogo/ZonaFormView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Catalogo/DimensionTematicaListView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Catalogo/DimensionTematicaFormView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Catalogo/OrganismoResponsableListView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Catalogo/OrganismoResponsableFormView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Catalogo/OrigenFinanciamientoListView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Catalogo/OrigenFinanciamientoFormView.axaml` (+ `.axaml.cs`)
- Modify: `src/StockApp.Presentation/App.axaml.cs`

**Interfaces:**
- Consumes: `ZonaListViewModel`/`ZonaFormViewModel`/etc. (Task 7); `ZonaApiClient`/etc. (Task 6); `IZonaService`/etc. (Task 4). Las vistas de FORMULARIO son molde EXACTO de `CategoriaFormView.axaml`/`.axaml.cs`. Las vistas de LISTADO son molde EXACTO de `UnidadMedidaListView.axaml`/`.axaml.cs` (`src/StockApp.Presentation/Views/Catalogo/`) — `DataGrid`, no `ListBox` (ver Global Constraints): `CategoriaListView.axaml` no se migró el 2026-08-24 y sigue en `ListBox`, pero un catálogo Nombre+Activo con columnas es una tabla, mismo criterio que justificó migrar Proveedores/UnidadMedida/PagosGasto ese día.
- Produces (consumido por Task 9): ocho `UserControl` resolubles por el `ViewLocator` a partir de sus ViewModels, cada `ListView` con `DataContextChanged` que dispara `CargarAsync()`.

No hay tests nuevos en este Task: ninguna vista de `Categoria`/`UnidadMedida` tiene test en `StockApp.Presentation.UiTests` (son `UserControl` sin comportamiento propio más allá de la carga inicial, ya cubierta por `CargarAsync` en Task 7). La verificación es: compila, y la suite completa de Presentation/UiTests sigue en verde (Step 3).

- [ ] **Step 1: Vistas de listado (`DataGrid`, molde de `UnidadMedidaListView` — el catálogo Nombre+Activo estructuralmente más parecido a los cuatro nuevos, ya migrado el 2026-08-24)**

Crear `src/StockApp.Presentation/Views/Catalogo/ZonaListView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Catalogo"
             xmlns:e="using:StockApp.Domain.Entities"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:c="using:StockApp.Presentation.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="400"
             x:Class="StockApp.Presentation.Views.Catalogo.ZonaListView"
             x:DataType="vm:ZonaListViewModel">

    <DockPanel Margin="{DynamicResource MargenVista}">

        <c:HeaderVista DockPanel.Dock="Top" Eyebrow="CATÁLOGO" Titulo="Zonas">
            <StackPanel Orientation="Horizontal" Spacing="{DynamicResource Espacio2}">
                <Button Classes="primary"
                        Content="Nueva zona"
                        Command="{Binding NuevoCommand}" />
                <Button Classes="secondary"
                        Content="Editar"
                        Command="{Binding EditarCommand}" />
                <Button Classes="secondary"
                        Content="Dar de baja"
                        Command="{Binding BajaCommand}" />
            </StackPanel>
        </c:HeaderVista>

        <Border Classes="card">
            <DockPanel>

                <!-- DataGrid (molde de UnidadMedidaListView.axaml, migrada 2026-08-24): el tema
                     global (Themes/DataGrid.axaml) da RowHeight/resize/FontSize gratis. Cada
                     celda de dato se atenúa individualmente con ActivoOpacidadConverter (no la
                     DataGridRow entera) para que el badge "Inactiva" quede a opacidad plena. -->
                <DataGrid ItemsSource="{Binding Items}"
                          SelectedItem="{Binding ItemSeleccionado}"
                          IsReadOnly="True"
                          CanUserSortColumns="True"
                          CanUserResizeColumns="True"
                          GridLinesVisibility="Horizontal"
                          ScrollViewer.HorizontalScrollBarVisibility="Auto">
                    <DataGrid.Columns>
                        <DataGridTemplateColumn Header="Nombre" Width="*" MinWidth="140" SortMemberPath="Nombre">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:Zona">
                                    <TextBlock Text="{Binding Nombre}"
                                               Opacity="{Binding Activo, Converter={x:Static conv:ActivoOpacidadConverter.Instance}}"
                                               TextTrimming="CharacterEllipsis"
                                               ToolTip.Tip="{Binding Nombre}"
                                               VerticalAlignment="Center" Margin="4,0" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="Estado" Width="Auto" CanUserSort="False">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:Zona">
                                    <c:BadgeEstado Texto="Inactiva" Tono="Neutro" IsVisible="{Binding !Activo}" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                    </DataGrid.Columns>
                </DataGrid>

            </DockPanel>
        </Border>

    </DockPanel>

</UserControl>
```

Crear `src/StockApp.Presentation/Views/Catalogo/ZonaListView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

public partial class ZonaListView : UserControl
{
    public ZonaListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ZonaListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

Crear `src/StockApp.Presentation/Views/Catalogo/DimensionTematicaListView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Catalogo"
             xmlns:e="using:StockApp.Domain.Entities"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:c="using:StockApp.Presentation.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="400"
             x:Class="StockApp.Presentation.Views.Catalogo.DimensionTematicaListView"
             x:DataType="vm:DimensionTematicaListViewModel">

    <DockPanel Margin="{DynamicResource MargenVista}">

        <c:HeaderVista DockPanel.Dock="Top" Eyebrow="CATÁLOGO" Titulo="Dimensiones">
            <StackPanel Orientation="Horizontal" Spacing="{DynamicResource Espacio2}">
                <Button Classes="primary"
                        Content="Nueva dimensión"
                        Command="{Binding NuevoCommand}" />
                <Button Classes="secondary"
                        Content="Editar"
                        Command="{Binding EditarCommand}" />
                <Button Classes="secondary"
                        Content="Dar de baja"
                        Command="{Binding BajaCommand}" />
            </StackPanel>
        </c:HeaderVista>

        <Border Classes="card">
            <DockPanel>

                <!-- DataGrid (molde de UnidadMedidaListView.axaml, migrada 2026-08-24): el tema
                     global (Themes/DataGrid.axaml) da RowHeight/resize/FontSize gratis. Cada
                     celda de dato se atenúa individualmente con ActivoOpacidadConverter (no la
                     DataGridRow entera) para que el badge "Inactiva" quede a opacidad plena. -->
                <DataGrid ItemsSource="{Binding Items}"
                          SelectedItem="{Binding ItemSeleccionado}"
                          IsReadOnly="True"
                          CanUserSortColumns="True"
                          CanUserResizeColumns="True"
                          GridLinesVisibility="Horizontal"
                          ScrollViewer.HorizontalScrollBarVisibility="Auto">
                    <DataGrid.Columns>
                        <DataGridTemplateColumn Header="Nombre" Width="*" MinWidth="140" SortMemberPath="Nombre">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:DimensionTematica">
                                    <TextBlock Text="{Binding Nombre}"
                                               Opacity="{Binding Activo, Converter={x:Static conv:ActivoOpacidadConverter.Instance}}"
                                               TextTrimming="CharacterEllipsis"
                                               ToolTip.Tip="{Binding Nombre}"
                                               VerticalAlignment="Center" Margin="4,0" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="Estado" Width="Auto" CanUserSort="False">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:DimensionTematica">
                                    <c:BadgeEstado Texto="Inactiva" Tono="Neutro" IsVisible="{Binding !Activo}" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                    </DataGrid.Columns>
                </DataGrid>

            </DockPanel>
        </Border>

    </DockPanel>

</UserControl>
```

Crear `src/StockApp.Presentation/Views/Catalogo/DimensionTematicaListView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

public partial class DimensionTematicaListView : UserControl
{
    public DimensionTematicaListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is DimensionTematicaListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

Crear `src/StockApp.Presentation/Views/Catalogo/OrganismoResponsableListView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Catalogo"
             xmlns:e="using:StockApp.Domain.Entities"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:c="using:StockApp.Presentation.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="400"
             x:Class="StockApp.Presentation.Views.Catalogo.OrganismoResponsableListView"
             x:DataType="vm:OrganismoResponsableListViewModel">

    <DockPanel Margin="{DynamicResource MargenVista}">

        <c:HeaderVista DockPanel.Dock="Top" Eyebrow="CATÁLOGO" Titulo="Organismos responsables">
            <StackPanel Orientation="Horizontal" Spacing="{DynamicResource Espacio2}">
                <Button Classes="primary"
                        Content="Nuevo organismo"
                        Command="{Binding NuevoCommand}" />
                <Button Classes="secondary"
                        Content="Editar"
                        Command="{Binding EditarCommand}" />
                <Button Classes="secondary"
                        Content="Dar de baja"
                        Command="{Binding BajaCommand}" />
            </StackPanel>
        </c:HeaderVista>

        <Border Classes="card">
            <DockPanel>

                <!-- DataGrid (molde de UnidadMedidaListView.axaml, migrada 2026-08-24): el tema
                     global (Themes/DataGrid.axaml) da RowHeight/resize/FontSize gratis. Cada
                     celda de dato se atenúa individualmente con ActivoOpacidadConverter (no la
                     DataGridRow entera) para que el badge "Inactivo" quede a opacidad plena. -->
                <DataGrid ItemsSource="{Binding Items}"
                          SelectedItem="{Binding ItemSeleccionado}"
                          IsReadOnly="True"
                          CanUserSortColumns="True"
                          CanUserResizeColumns="True"
                          GridLinesVisibility="Horizontal"
                          ScrollViewer.HorizontalScrollBarVisibility="Auto">
                    <DataGrid.Columns>
                        <DataGridTemplateColumn Header="Nombre" Width="*" MinWidth="140" SortMemberPath="Nombre">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:OrganismoResponsable">
                                    <TextBlock Text="{Binding Nombre}"
                                               Opacity="{Binding Activo, Converter={x:Static conv:ActivoOpacidadConverter.Instance}}"
                                               TextTrimming="CharacterEllipsis"
                                               ToolTip.Tip="{Binding Nombre}"
                                               VerticalAlignment="Center" Margin="4,0" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="Estado" Width="Auto" CanUserSort="False">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:OrganismoResponsable">
                                    <c:BadgeEstado Texto="Inactivo" Tono="Neutro" IsVisible="{Binding !Activo}" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                    </DataGrid.Columns>
                </DataGrid>

            </DockPanel>
        </Border>

    </DockPanel>

</UserControl>
```

Crear `src/StockApp.Presentation/Views/Catalogo/OrganismoResponsableListView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

public partial class OrganismoResponsableListView : UserControl
{
    public OrganismoResponsableListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is OrganismoResponsableListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

Crear `src/StockApp.Presentation/Views/Catalogo/OrigenFinanciamientoListView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Catalogo"
             xmlns:e="using:StockApp.Domain.Entities"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:c="using:StockApp.Presentation.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="400"
             x:Class="StockApp.Presentation.Views.Catalogo.OrigenFinanciamientoListView"
             x:DataType="vm:OrigenFinanciamientoListViewModel">

    <DockPanel Margin="{DynamicResource MargenVista}">

        <c:HeaderVista DockPanel.Dock="Top" Eyebrow="CATÁLOGO" Titulo="Orígenes de financiamiento">
            <StackPanel Orientation="Horizontal" Spacing="{DynamicResource Espacio2}">
                <Button Classes="primary"
                        Content="Nuevo origen"
                        Command="{Binding NuevoCommand}" />
                <Button Classes="secondary"
                        Content="Editar"
                        Command="{Binding EditarCommand}" />
                <Button Classes="secondary"
                        Content="Dar de baja"
                        Command="{Binding BajaCommand}" />
            </StackPanel>
        </c:HeaderVista>

        <Border Classes="card">
            <DockPanel>

                <!-- DataGrid (molde de UnidadMedidaListView.axaml, migrada 2026-08-24): el tema
                     global (Themes/DataGrid.axaml) da RowHeight/resize/FontSize gratis. Cada
                     celda de dato se atenúa individualmente con ActivoOpacidadConverter (no la
                     DataGridRow entera) para que el badge "Inactivo" quede a opacidad plena. -->
                <DataGrid ItemsSource="{Binding Items}"
                          SelectedItem="{Binding ItemSeleccionado}"
                          IsReadOnly="True"
                          CanUserSortColumns="True"
                          CanUserResizeColumns="True"
                          GridLinesVisibility="Horizontal"
                          ScrollViewer.HorizontalScrollBarVisibility="Auto">
                    <DataGrid.Columns>
                        <DataGridTemplateColumn Header="Nombre" Width="*" MinWidth="140" SortMemberPath="Nombre">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:OrigenFinanciamiento">
                                    <TextBlock Text="{Binding Nombre}"
                                               Opacity="{Binding Activo, Converter={x:Static conv:ActivoOpacidadConverter.Instance}}"
                                               TextTrimming="CharacterEllipsis"
                                               ToolTip.Tip="{Binding Nombre}"
                                               VerticalAlignment="Center" Margin="4,0" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="Estado" Width="Auto" CanUserSort="False">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="e:OrigenFinanciamiento">
                                    <c:BadgeEstado Texto="Inactivo" Tono="Neutro" IsVisible="{Binding !Activo}" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                    </DataGrid.Columns>
                </DataGrid>

            </DockPanel>
        </Border>

    </DockPanel>

</UserControl>
```

Crear `src/StockApp.Presentation/Views/Catalogo/OrigenFinanciamientoListView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

public partial class OrigenFinanciamientoListView : UserControl
{
    public OrigenFinanciamientoListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is OrigenFinanciamientoListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

- [ ] **Step 2: Vistas de formulario (molde de `CategoriaFormView`)**

Crear `src/StockApp.Presentation/Views/Catalogo/ZonaFormView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Catalogo"
             xmlns:c="using:StockApp.Presentation.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="400" d:DesignHeight="250"
             x:Class="StockApp.Presentation.Views.Catalogo.ZonaFormView"
             x:DataType="vm:ZonaFormViewModel">

    <Border Classes="card" Padding="{DynamicResource MargenVista}" Margin="{DynamicResource MargenVista}"
            MaxWidth="380" HorizontalAlignment="Center" VerticalAlignment="Center">
        <StackPanel Spacing="{DynamicResource Espacio3}">

            <c:HeaderVista Titulo="{Binding Titulo}" />

            <c:CampoFormulario Etiqueta="Nombre" Requerido="True">
                <TextBox Text="{Binding Nombre}"
                         PlaceholderText="Nombre de la zona" />
            </c:CampoFormulario>

            <TextBlock Text="{Binding MensajeError}"
                       Foreground="{DynamicResource DangerBrush}"
                       IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                       TextWrapping="Wrap" />

            <Button Classes="primary"
                    Content="Guardar"
                    Command="{Binding GuardarCommand}"
                    HorizontalAlignment="Stretch"
                    HorizontalContentAlignment="Center" />

        </StackPanel>
    </Border>

</UserControl>
```

Crear `src/StockApp.Presentation/Views/Catalogo/ZonaFormView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Catalogo;

public partial class ZonaFormView : UserControl
{
    public ZonaFormView()
    {
        InitializeComponent();
    }
}
```

Crear `src/StockApp.Presentation/Views/Catalogo/DimensionTematicaFormView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Catalogo"
             xmlns:c="using:StockApp.Presentation.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="400" d:DesignHeight="250"
             x:Class="StockApp.Presentation.Views.Catalogo.DimensionTematicaFormView"
             x:DataType="vm:DimensionTematicaFormViewModel">

    <Border Classes="card" Padding="{DynamicResource MargenVista}" Margin="{DynamicResource MargenVista}"
            MaxWidth="380" HorizontalAlignment="Center" VerticalAlignment="Center">
        <StackPanel Spacing="{DynamicResource Espacio3}">

            <c:HeaderVista Titulo="{Binding Titulo}" />

            <c:CampoFormulario Etiqueta="Nombre" Requerido="True">
                <TextBox Text="{Binding Nombre}"
                         PlaceholderText="Nombre de la dimensión" />
            </c:CampoFormulario>

            <TextBlock Text="{Binding MensajeError}"
                       Foreground="{DynamicResource DangerBrush}"
                       IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                       TextWrapping="Wrap" />

            <Button Classes="primary"
                    Content="Guardar"
                    Command="{Binding GuardarCommand}"
                    HorizontalAlignment="Stretch"
                    HorizontalContentAlignment="Center" />

        </StackPanel>
    </Border>

</UserControl>
```

Crear `src/StockApp.Presentation/Views/Catalogo/DimensionTematicaFormView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Catalogo;

public partial class DimensionTematicaFormView : UserControl
{
    public DimensionTematicaFormView()
    {
        InitializeComponent();
    }
}
```

Crear `src/StockApp.Presentation/Views/Catalogo/OrganismoResponsableFormView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Catalogo"
             xmlns:c="using:StockApp.Presentation.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="400" d:DesignHeight="250"
             x:Class="StockApp.Presentation.Views.Catalogo.OrganismoResponsableFormView"
             x:DataType="vm:OrganismoResponsableFormViewModel">

    <Border Classes="card" Padding="{DynamicResource MargenVista}" Margin="{DynamicResource MargenVista}"
            MaxWidth="380" HorizontalAlignment="Center" VerticalAlignment="Center">
        <StackPanel Spacing="{DynamicResource Espacio3}">

            <c:HeaderVista Titulo="{Binding Titulo}" />

            <c:CampoFormulario Etiqueta="Nombre" Requerido="True">
                <TextBox Text="{Binding Nombre}"
                         PlaceholderText="Nombre del organismo responsable" />
            </c:CampoFormulario>

            <TextBlock Text="{Binding MensajeError}"
                       Foreground="{DynamicResource DangerBrush}"
                       IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                       TextWrapping="Wrap" />

            <Button Classes="primary"
                    Content="Guardar"
                    Command="{Binding GuardarCommand}"
                    HorizontalAlignment="Stretch"
                    HorizontalContentAlignment="Center" />

        </StackPanel>
    </Border>

</UserControl>
```

Crear `src/StockApp.Presentation/Views/Catalogo/OrganismoResponsableFormView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Catalogo;

public partial class OrganismoResponsableFormView : UserControl
{
    public OrganismoResponsableFormView()
    {
        InitializeComponent();
    }
}
```

Crear `src/StockApp.Presentation/Views/Catalogo/OrigenFinanciamientoFormView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Catalogo"
             xmlns:c="using:StockApp.Presentation.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="400" d:DesignHeight="250"
             x:Class="StockApp.Presentation.Views.Catalogo.OrigenFinanciamientoFormView"
             x:DataType="vm:OrigenFinanciamientoFormViewModel">

    <Border Classes="card" Padding="{DynamicResource MargenVista}" Margin="{DynamicResource MargenVista}"
            MaxWidth="380" HorizontalAlignment="Center" VerticalAlignment="Center">
        <StackPanel Spacing="{DynamicResource Espacio3}">

            <c:HeaderVista Titulo="{Binding Titulo}" />

            <c:CampoFormulario Etiqueta="Nombre" Requerido="True">
                <TextBox Text="{Binding Nombre}"
                         PlaceholderText="Nombre del origen de financiamiento" />
            </c:CampoFormulario>

            <TextBlock Text="{Binding MensajeError}"
                       Foreground="{DynamicResource DangerBrush}"
                       IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                       TextWrapping="Wrap" />

            <Button Classes="primary"
                    Content="Guardar"
                    Command="{Binding GuardarCommand}"
                    HorizontalAlignment="Stretch"
                    HorizontalContentAlignment="Center" />

        </StackPanel>
    </Border>

</UserControl>
```

Crear `src/StockApp.Presentation/Views/Catalogo/OrigenFinanciamientoFormView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Catalogo;

public partial class OrigenFinanciamientoFormView : UserControl
{
    public OrigenFinanciamientoFormView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: DI en `App.axaml.cs`**

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar DEBAJO de "services.AddTransient<IUnidadMedidaService, UnidadMedidaApiClient>();"
// (mismo bloque de ApiClients de Fase 3b):

        services.AddTransient<IZonaService, ZonaApiClient>();
        services.AddTransient<IDimensionTematicaService, DimensionTematicaApiClient>();
        services.AddTransient<IOrganismoResponsableService, OrganismoResponsableApiClient>();
        services.AddTransient<IOrigenFinanciamientoService, OrigenFinanciamientoApiClient>();
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar DEBAJO de "services.AddTransient<UnidadMedidaFormViewModel>();"
// (mismo bloque de VMs de catálogo):

        services.AddTransient<ZonaListViewModel>();
        services.AddTransient<ZonaFormViewModel>();
        services.AddTransient<DimensionTematicaListViewModel>();
        services.AddTransient<DimensionTematicaFormViewModel>();
        services.AddTransient<OrganismoResponsableListViewModel>();
        services.AddTransient<OrganismoResponsableFormViewModel>();
        services.AddTransient<OrigenFinanciamientoListViewModel>();
        services.AddTransient<OrigenFinanciamientoFormViewModel>();
```

- [ ] **Step 4: Correr toda la suite de `StockApp.Presentation.Tests` y compilar `StockApp.Presentation`**

Run: `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj`
Expected: build exitoso — confirma que las ocho vistas axaml compilan (bindings tipados con `x:DataType` fallan en build, no en runtime, según el gotcha conocido del repo) y que `App.axaml.cs` resuelve todos los tipos nuevos.

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: PASS — todos los tests en verde, sin regresiones.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Views/Catalogo/ZonaListView.axaml \
        src/StockApp.Presentation/Views/Catalogo/ZonaListView.axaml.cs \
        src/StockApp.Presentation/Views/Catalogo/ZonaFormView.axaml \
        src/StockApp.Presentation/Views/Catalogo/ZonaFormView.axaml.cs \
        src/StockApp.Presentation/Views/Catalogo/DimensionTematicaListView.axaml \
        src/StockApp.Presentation/Views/Catalogo/DimensionTematicaListView.axaml.cs \
        src/StockApp.Presentation/Views/Catalogo/DimensionTematicaFormView.axaml \
        src/StockApp.Presentation/Views/Catalogo/DimensionTematicaFormView.axaml.cs \
        src/StockApp.Presentation/Views/Catalogo/OrganismoResponsableListView.axaml \
        src/StockApp.Presentation/Views/Catalogo/OrganismoResponsableListView.axaml.cs \
        src/StockApp.Presentation/Views/Catalogo/OrganismoResponsableFormView.axaml \
        src/StockApp.Presentation/Views/Catalogo/OrganismoResponsableFormView.axaml.cs \
        src/StockApp.Presentation/Views/Catalogo/OrigenFinanciamientoListView.axaml \
        src/StockApp.Presentation/Views/Catalogo/OrigenFinanciamientoListView.axaml.cs \
        src/StockApp.Presentation/Views/Catalogo/OrigenFinanciamientoFormView.axaml \
        src/StockApp.Presentation/Views/Catalogo/OrigenFinanciamientoFormView.axaml.cs \
        src/StockApp.Presentation/App.axaml.cs
git commit -m "feat(catalogo): agrega vistas y DI de los clasificadores de tareas"
```

---

### Task 9: `CatalogosTareaView` — pantalla que hostea los cuatro catálogos en pestañas

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Catalogo/CatalogosTareaViewModel.cs`
- Create: `src/StockApp.Presentation/Views/Catalogo/CatalogosTareaView.axaml`
- Create: `src/StockApp.Presentation/Views/Catalogo/CatalogosTareaView.axaml.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/CatalogosTareaViewModelTests.cs`

**Interfaces:**
- Consumes: `ZonaListViewModel`/`DimensionTematicaListViewModel`/`OrganismoResponsableListViewModel`/`OrigenFinanciamientoListViewModel` (Task 7), resueltos por DI. Molde EXACTO de `MaestrosFinanzasViewModel`/`MaestrosFinanzasView.axaml` (`src/StockApp.Presentation/ViewModels/Finanzas/MaestrosFinanzasViewModel.cs`, `src/StockApp.Presentation/Views/Finanzas/MaestrosFinanzasView.axaml`) — `TabControl` con cuatro `TabItem`, cada uno hospedando una `XxxListView` con su propio `DataContext`.
- Produces (consumido por Task 10): `CatalogosTareaViewModel` (`ZonasVm`, `DimensionesVm`, `OrganismosVm`, `OrigenesVm`) resoluble por `INavigationService`/`ViewLocator`.

**Nota:** `MaestrosFinanzasViewModel` (la plantilla exacta) NO tiene test propio en el repo — es una composición trivial de sub-VMs sin lógica. Acá se agrega un test mínimo de todas formas (constructor smoke test) para cerrar el ciclo TDD de la Task; no rompe el molde, solo lo refuerza.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/CatalogosTareaViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class CatalogosTareaViewModelTests
{
    [Fact]
    public void Constructor_ExponeLosCuatroSubViewModelsInyectados()
    {
        var zonasVm = TestFactories.ZonaListViewModelVacio();
        var dimensionesVm = TestFactories.DimensionTematicaListViewModelVacio();
        var organismosVm = TestFactories.OrganismoResponsableListViewModelVacio();
        var origenesVm = TestFactories.OrigenFinanciamientoListViewModelVacio();

        var vm = new CatalogosTareaViewModel(zonasVm, dimensionesVm, organismosVm, origenesVm);

        Assert.Same(zonasVm, vm.ZonasVm);
        Assert.Same(dimensionesVm, vm.DimensionesVm);
        Assert.Same(organismosVm, vm.OrganismosVm);
        Assert.Same(origenesVm, vm.OrigenesVm);
    }
}

/// <summary>
/// Fábricas mínimas para construir los cuatro ListViewModel con dependencias mockeadas,
/// sin repetir el helper Crear() de cada archivo de test de Task 7 (que es privado a su clase).
/// </summary>
internal static class TestFactories
{
    public static ZonaListViewModel ZonaListViewModelVacio()
    {
        var svc = new Moq.Mock<StockApp.Application.Catalogo.IZonaService>();
        svc.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<StockApp.Domain.Entities.Zona>());
        return new ZonaListViewModel(
            svc.Object,
            Moq.Mock.Of<StockApp.Presentation.Navigation.INavigationService>(),
            Moq.Mock.Of<StockApp.Presentation.Services.IConfirmacionService>());
    }

    public static DimensionTematicaListViewModel DimensionTematicaListViewModelVacio()
    {
        var svc = new Moq.Mock<StockApp.Application.Catalogo.IDimensionTematicaService>();
        svc.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<StockApp.Domain.Entities.DimensionTematica>());
        return new DimensionTematicaListViewModel(
            svc.Object,
            Moq.Mock.Of<StockApp.Presentation.Navigation.INavigationService>(),
            Moq.Mock.Of<StockApp.Presentation.Services.IConfirmacionService>());
    }

    public static OrganismoResponsableListViewModel OrganismoResponsableListViewModelVacio()
    {
        var svc = new Moq.Mock<StockApp.Application.Catalogo.IOrganismoResponsableService>();
        svc.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<StockApp.Domain.Entities.OrganismoResponsable>());
        return new OrganismoResponsableListViewModel(
            svc.Object,
            Moq.Mock.Of<StockApp.Presentation.Navigation.INavigationService>(),
            Moq.Mock.Of<StockApp.Presentation.Services.IConfirmacionService>());
    }

    public static OrigenFinanciamientoListViewModel OrigenFinanciamientoListViewModelVacio()
    {
        var svc = new Moq.Mock<StockApp.Application.Catalogo.IOrigenFinanciamientoService>();
        svc.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<StockApp.Domain.Entities.OrigenFinanciamiento>());
        return new OrigenFinanciamientoListViewModel(
            svc.Object,
            Moq.Mock.Of<StockApp.Presentation.Navigation.INavigationService>(),
            Moq.Mock.Of<StockApp.Presentation.Services.IConfirmacionService>());
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter FullyQualifiedName~CatalogosTareaViewModelTests`
Expected: FALLA de compilación — `StockApp.Presentation.ViewModels.Catalogo.CatalogosTareaViewModel` no existe.

- [ ] **Step 3: Implementación del ViewModel**

Crear `src/StockApp.Presentation/ViewModels/Catalogo/CatalogosTareaViewModel.cs`:

```csharp
namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Pantalla "Catálogos de tareas" (spec 2026-09-08, D2): hostea las cuatro sub-listas
/// (zonas, dimensiones temáticas, organismos responsables, orígenes de financiamiento) que
/// la vista muestra en tabs. Los formularios de alta/edición navegan a pantalla completa y
/// vuelven acá al guardar o cancelar — mismo patrón que MaestrosFinanzasViewModel.
/// </summary>
public partial class CatalogosTareaViewModel : ViewModelBase
{
    public ZonaListViewModel ZonasVm { get; }
    public DimensionTematicaListViewModel DimensionesVm { get; }
    public OrganismoResponsableListViewModel OrganismosVm { get; }
    public OrigenFinanciamientoListViewModel OrigenesVm { get; }

    public CatalogosTareaViewModel(
        ZonaListViewModel zonasVm,
        DimensionTematicaListViewModel dimensionesVm,
        OrganismoResponsableListViewModel organismosVm,
        OrigenFinanciamientoListViewModel origenesVm)
    {
        ZonasVm       = zonasVm;
        DimensionesVm = dimensionesVm;
        OrganismosVm  = organismosVm;
        OrigenesVm    = origenesVm;
    }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter FullyQualifiedName~CatalogosTareaViewModelTests`
Expected: PASS — 1 test verde.

- [ ] **Step 5: Vista `CatalogosTareaView.axaml` (molde de `MaestrosFinanzasView.axaml`)**

Crear `src/StockApp.Presentation/Views/Catalogo/CatalogosTareaView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Catalogo"
             xmlns:views="using:StockApp.Presentation.Views.Catalogo"
             xmlns:c="using:StockApp.Presentation.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="600"
             x:Class="StockApp.Presentation.Views.Catalogo.CatalogosTareaView"
             x:DataType="vm:CatalogosTareaViewModel">

    <DockPanel Margin="{DynamicResource MargenVista}">

        <c:HeaderVista DockPanel.Dock="Top" Eyebrow="TAREAS" Titulo="Catálogos de tareas" />

        <TabControl>
            <TabItem Header="Zonas">
                <views:ZonaListView DataContext="{Binding ZonasVm}" />
            </TabItem>
            <TabItem Header="Dimensiones">
                <views:DimensionTematicaListView DataContext="{Binding DimensionesVm}" />
            </TabItem>
            <TabItem Header="Organismos responsables">
                <views:OrganismoResponsableListView DataContext="{Binding OrganismosVm}" />
            </TabItem>
            <TabItem Header="Orígenes de financiamiento">
                <views:OrigenFinanciamientoListView DataContext="{Binding OrigenesVm}" />
            </TabItem>
        </TabControl>

    </DockPanel>

</UserControl>
```

Crear `src/StockApp.Presentation/Views/Catalogo/CatalogosTareaView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Catalogo;

public partial class CatalogosTareaView : UserControl
{
    public CatalogosTareaView()
    {
        InitializeComponent();
        // La carga de datos la cablea cada sub-vista (XxxListView) en su propio
        // DataContextChanged (Task 8) — acá no hay nada que inicializar.
    }
}
```

- [ ] **Step 6: DI en `App.axaml.cs`**

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar DEBAJO de "services.AddTransient<OrigenFinanciamientoFormViewModel>();"
// (justo después del bloque de VMs de catálogo agregado en la Task 8):

        services.AddTransient<CatalogosTareaViewModel>();
```

- [ ] **Step 7: Correr toda la suite de `StockApp.Presentation.Tests` y compilar**

Run: `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj`
Expected: build exitoso.

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: PASS — todos los tests en verde, sin regresiones.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Catalogo/CatalogosTareaViewModel.cs \
        src/StockApp.Presentation/Views/Catalogo/CatalogosTareaView.axaml \
        src/StockApp.Presentation/Views/Catalogo/CatalogosTareaView.axaml.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Catalogo/CatalogosTareaViewModelTests.cs
git commit -m "feat(catalogo): agrega pantalla CatalogosTareaView con las cuatro pestanas"
```

---

### Task 10: Ítem en el menú lateral

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelGruposTests.cs`

**Interfaces:**
- Consumes: `CatalogosTareaViewModel` (Task 9); `PuedeGestionarTablasMaestras` (propiedad YA existente en `ShellMainViewModel.cs:89-90`, gateada por `Permisos.GestionarTablasMaestras` — se reutiliza tal cual, no se crea una propiedad `Puede*` nueva); `CrearItem(...)` (helper existente que registra el ítem en `_itemsGateados` para `RecalcularVisibilidad`).
- Produces: `ShellMainViewModel.NavCatalogosTareaCommand`; un `ItemNavegacion` nuevo dentro del grupo "Tablas maestras" (`Grupos`), sección `"CatalogosTarea"`.

**Nota:** el sidebar de este repo es data-driven (`Grupos`/`ItemNavegacion`, `ItemsControl` en `ShellMainView.axaml` — Ruling 2026-08-19, ver `src/StockApp.Presentation/ViewModels/ItemNavegacion.cs`), no una lista de `Button` hardcodeados por sección. Agregar un ítem NO requiere tocar `ShellMainView.axaml`: alcanza con sumar una llamada a `CrearItem(...)` dentro del grupo `"Tablas maestras"` en el constructor de `ShellMainViewModel`.

- [ ] **Step 1: Escribir los tests que fallan**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs
// Agregar junto a los tests de NavCategorias (después de "NavCategorias_EstableceSeccionActiva_Categorias"):

    [Fact]
    public void NavCatalogosTarea_Admin_LlamaNavegar_ACatalogosTareaViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavCatalogosTareaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<StockApp.Presentation.ViewModels.Catalogo.CatalogosTareaViewModel>(), Times.Once);
    }

    [Fact]
    public void NavCatalogosTarea_EstableceSeccionActiva_CatalogosTarea()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavCatalogosTareaCommand.Execute(null);

        Assert.Equal("CatalogosTarea", vm.SeccionActiva);
    }
```

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelGruposTests.cs
// Agregar como test nuevo (verifica que el ítem cae dentro de "Tablas maestras" y respeta
// el MISMO permiso que Categorías/Proveedores/Unidades de medida):

    [Fact]
    public void GrupoTablasMaestras_TieneElItemCatalogosTarea()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        var tablasMaestras = vm.Grupos.Single(g => g.Titulo == "Tablas maestras");

        Assert.Contains(tablasMaestras.ItemsVisibles, i => i.Seccion == "CatalogosTarea");
    }

    [Fact]
    public void GrupoTablasMaestras_OperadorSinPermiso_NoVeCatalogosTarea()
    {
        var (vm, _, _) = Crear(RolUsuario.Operador);

        var tablasMaestras = vm.Grupos.Single(g => g.Titulo == "Tablas maestras");

        Assert.DoesNotContain(tablasMaestras.ItemsVisibles, i => i.Seccion == "CatalogosTarea");
    }

    [Fact]
    public void GrupoTablasMaestras_OperadorConGestionarTablasMaestras_VeCatalogosTarea()
    {
        var (vm, _, _) = Crear(RolUsuario.Operador, new[] { Permisos.GestionarTablasMaestras });

        var tablasMaestras = vm.Grupos.Single(g => g.Titulo == "Tablas maestras");

        Assert.Contains(tablasMaestras.ItemsVisibles, i => i.Seccion == "CatalogosTarea");
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NavCatalogosTarea|FullyQualifiedName~GrupoTablasMaestras_TieneElItemCatalogosTarea|FullyQualifiedName~GrupoTablasMaestras_OperadorSinPermiso_NoVeCatalogosTarea|FullyQualifiedName~GrupoTablasMaestras_OperadorConGestionarTablasMaestras_VeCatalogosTarea"`
Expected: FALLA de compilación — `ShellMainViewModel.NavCatalogosTareaCommand` no existe, y ningún ítem con `Seccion == "CatalogosTarea"` existe en `Grupos`.

- [ ] **Step 3: Agregar el using, el ítem y el comando**

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
// Agregar junto al resto de usings ViewModels.*:

using StockApp.Presentation.ViewModels.Catalogo;
```

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
// Dentro del constructor, en el grupo "Tablas maestras" (Grupos), agregar DEBAJO de la línea
// que crea el ítem "Unidades de medida":

            new GrupoNavegacion("Tablas maestras", new List<ItemNavegacion>
            {
                CrearItem("Categorías", "mdi-shape", NavCategoriasCommand, "Categorias", () => PuedeGestionarTablasMaestras),
                CrearItem("Proveedores", "mdi-truck", NavProveedoresCommand, "Proveedores", () => PuedeGestionarTablasMaestras),
                CrearItem("Unidades de medida", "mdi-ruler", NavUnidadesMedidaCommand, "UnidadesMedida", () => PuedeGestionarTablasMaestras),
                CrearItem("Catálogos de tareas", "mdi-tag-multiple", NavCatalogosTareaCommand, "CatalogosTarea", () => PuedeGestionarTablasMaestras),
            }),
```

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
// Agregar DEBAJO del comando NavUnidadesMedida (mismo bloque de comandos de "Tablas maestras"):

    [RelayCommand]
    private void NavCatalogosTarea()
    {
        SeccionActiva = "CatalogosTarea";
        _navigation.Navegar<CatalogosTareaViewModel>();
    }
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NavCatalogosTarea|FullyQualifiedName~GrupoTablasMaestras_TieneElItemCatalogosTarea|FullyQualifiedName~GrupoTablasMaestras_OperadorSinPermiso_NoVeCatalogosTarea|FullyQualifiedName~GrupoTablasMaestras_OperadorConGestionarTablasMaestras_VeCatalogosTarea"`
Expected: PASS — 5 tests verdes.

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: PASS — todos los tests en verde, incluido `Grupos_ParaUnAdmin_TieneLosOchoGrupos` (sigue habiendo 8 grupos: el ítem nuevo entra DENTRO de "Tablas maestras", no crea un grupo).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelGruposTests.cs
git commit -m "feat(catalogo): agrega Catalogos de tareas al menu lateral"
```

---

### Task 11: Suite completa + verificación orgánica con la app real

**Files:** ninguno (solo comandos y checklist manual).

**Interfaces:**
- Consumes: todo lo producido en Tasks 1-10.
- Produces: confirmación de que el módulo está completo y cerrado — no hay Task 12.

- [ ] **Step 1: Suite completa de los 7 proyectos de test, SECUENCIAL**

`StockApp.Application.Tests` y `StockApp.Api.Tests` NUNCA en paralelo (Testcontainers). `StockApp.Infrastructure.Tests` necesita el contenedor `stockapp-pg` levantado.

```bash
dotnet test tests/StockApp.Domain.Tests
dotnet test tests/StockApp.Application.Tests
dotnet test tests/StockApp.Infrastructure.Tests
dotnet test tests/StockApp.Api.Tests
dotnet test tests/StockApp.ApiClient.Tests
dotnet test tests/StockApp.Presentation.Tests
dotnet test tests/StockApp.Presentation.UiTests
```
Expected: PASS en los 7 proyectos, sin regresiones sobre la línea base previa a este plan. (`StockApp.Presentation.UiTests` no tiene tests nuevos de este plan — corre para confirmar que nada de lo tocado en `App.axaml.cs`/`ShellMainViewModel.cs` rompió sus guardianes existentes, ej. `ReflexionVistaViewModelTests`/`ShellMainViewGatesTests`.)

- [ ] **Step 2: Checklist de verificación orgánica con la app real**

No se da por cerrado el plan solo con tests verdes (convención del proyecto: "usuario reporta = ya probó" corre en ambos sentidos — antes de decir "listo" hay que haberlo tocado). Con la API y el desktop reales corriendo (Postgres del proyecto levantado, contenedor `stockapp-pg`):

- [ ] Login como Admin. El ítem "Catálogos de tareas" aparece en el menú lateral, dentro de la sección "Tablas maestras" (junto a Categorías/Proveedores/Unidades de medida).
- [ ] Abrir "Catálogos de tareas": aparecen las cuatro pestañas — Zonas, Dimensiones, Organismos responsables, Orígenes de financiamiento — cada una vacía al principio.
- [ ] En la pestaña Zonas: "Nueva zona" → cargar nombre "Centro" → Guardar. Vuelve al listado y "Centro" aparece.
- [ ] Editar "Centro" → renombrar a "Centro Histórico" → Guardar. El listado refleja el cambio.
- [ ] Dar de baja "Centro Histórico": pide confirmación: cancelar no debe dar de baja; confirmar sí — la fila queda atenuada con el badge "Inactiva", y los botones "Editar"/"Dar de baja" quedan deshabilitados con esa fila seleccionada.
- [ ] Intentar crear una zona con el mismo nombre que una ya existente (activa): el formulario muestra el mensaje de error del servidor ("Ya existe una zona con el nombre...") sin navegar al listado.
- [ ] Repetir el ciclo alta/edición/baja/duplicado en las otras tres pestañas (Dimensiones, Organismos responsables, Orígenes de financiamiento) — confirmar que cada una es independiente (dar de baja algo en Zonas no afecta a Dimensiones).
- [ ] Crear un usuario Operador SIN `catalogo.maestras` (permiso `GestionarTablasMaestras`) y loguearlo: el ítem "Catálogos de tareas" no aparece en el menú, igual que Categorías/Proveedores/Unidades de medida.
- [ ] Desde el panel de permisos (Admin), tildar `catalogo.maestras` para ese Operador y volver a loguearlo: el ítem aparece y las cuatro pestañas funcionan igual que para Admin (el permiso, no el rol, es lo que gatea).
- [ ] Confirmar en el log de auditoría (Admin, pantalla existente) que las altas/bajas/modificaciones de los cuatro catálogos quedan registradas con su acción (`AltaZona`, `BajaDimensionTematica`, etc.) y el usuario correcto.

- [ ] **Step 3: Cierre**

Si el Step 1 y el Step 2 pasan sin hallazgos, el plan queda completo. No hay merge a `main` en este plan — el usuario hace el merge cuando lo decida (Global Constraints: rama `feat/tareas-catalogos`, sin push).

---

## Índice de tareas

1. [Task 1: Entidades de dominio — `Zona`, `DimensionTematica`, `OrganismoResponsable`, `OrigenFinanciamiento`](#task-1-entidades-de-dominio-zona-dimensiontematica-organismoresponsable-origenfinanciamiento)
2. [Task 2: Configuración EF en `AppDbContext` + migración + limpieza de fixtures de test](#task-2-configuración-ef-en-appdbcontext-migración-limpieza-de-fixtures-de-test)
3. [Task 3: Repositorios — interfaces + implementaciones EF de los cuatro catálogos](#task-3-repositorios-interfaces-implementaciones-ef-de-los-cuatro-catálogos)
4. [Task 4: Valores de auditoría + servicios de Application (sin invalidación de cache, D14)](#task-4-valores-de-auditoría-servicios-de-application-sin-invalidación-de-cache-d14)
5. [Task 5: Api — endpoints de los cuatro catálogos + registro en `Program.cs`](#task-5-api-endpoints-de-los-cuatro-catálogos-registro-en-programcs)
6. [Task 6: ApiClient — los cuatro clientes HTTP](#task-6-apiclient-los-cuatro-clientes-http)
7. [Task 7: Presentation — ViewModels de listado y formulario de los cuatro catálogos](#task-7-presentation-viewmodels-de-listado-y-formulario-de-los-cuatro-catálogos)
8. [Task 8: Presentation — Vistas axaml de los cuatro catálogos + registro DI](#task-8-presentation-vistas-axaml-de-los-cuatro-catálogos-registro-di)
9. [Task 9: `CatalogosTareaView` — pantalla que hostea los cuatro catálogos en pestañas](#task-9-catalogostareaview-pantalla-que-hostea-los-cuatro-catálogos-en-pestañas)
10. [Task 10: Ítem en el menú lateral](#task-10-ítem-en-el-menú-lateral)
11. [Task 11: Suite completa + verificación orgánica con la app real](#task-11-suite-completa-verificación-orgánica-con-la-app-real)
