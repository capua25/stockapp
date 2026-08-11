# Permisos por operador — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reemplazar la autorización binaria por rol (`Admin`/`Operador` fijo) por permisos configurables individualmente por el Admin para cada usuario Operador, sin romper ninguna de las dos barreras de autorización existentes (HTTP + Application) ni los ~2447 tests hoy verdes.

**Architecture:** Nueva tabla `PermisosUsuario(UsuarioId, Permiso)` es la única fuente de verdad para los 11 permisos configurables; 4 permisos (`GestionarUsuarios`, `ImportarPlanillas`, `GestionarDiagnostico`, `AdministrarTareas`) quedan estructuralmente Admin-only y nunca tocan esa tabla. Un `IProveedorPermisos` singleton cachea en memoria por `usuarioId` (invalidado al guardar), consultado tanto por un `PermisoAuthorizationHandler` nuevo (barrera HTTP, reemplaza el bloque de policies de `Program.cs`) como por un middleware que puebla `ICurrentSession.PermisosActuales` una vez por request — así `IAuthorizationService.Verificar` sigue siendo 100% sincrónico pese a que la fuente de verdad ahora vive en Postgres.

**Tech Stack:** .NET / C#, EF Core + Npgsql (Postgres), ASP.NET Core Minimal APIs + `AuthorizationHandler`, Avalonia + CommunityToolkit.Mvvm, xUnit, Moq (solo en Presentation.Tests), Testcontainers.

**Spec:** `docs/superpowers/specs/2026-08-10-permisos-por-operador-design.md`

## Global Constraints

- **Idioma**: identificadores, comentarios y mensajes de usuario en español, como todo el repo.
- **Commits**: conventional commits en español. **Nunca** agregar `Co-Authored-By` ni atribución a IA.
- **Nunca correr `dotnet build` como paso separado** (convención del usuario) — los `dotnet test` ya compilan lo que tocan.
- **Tests**: xUnit con `Assert.*`. Fakes manuales escritos a mano en `Application.Tests`, `Api.Tests`, `ApiClient.Tests` e `Infrastructure.Tests`. **Moq solo en `StockApp.Presentation.Tests`**.
- **Los 4 permisos estructurales** (`GestionarUsuarios`, `ImportarPlanillas`, `GestionarDiagnostico`, `AdministrarTareas`) se cortan **ANTES** de consultar `PermisosActuales`/`IProveedorPermisos`, nunca después — tanto en `AuthorizationService.Verificar` como en `PermisoAuthorizationHandler`. Invertir ese orden es el bug de seguridad más peligroso posible en este cambio (ver spec, sección "Riesgos").
- **`IAuthorizationService.Verificar` permanece SINCRÓNICO.** Ninguna task puede introducir `async`/`await` en su firma ni propagarlo a los ~96 call sites. El único punto de I/O asíncrono de todo el diseño del lado Application es el middleware de la Task 8.
- **Fail-closed**: sin filas en `PermisosUsuario` para un usuario = sin permisos configurables para ese usuario. Nunca un default permisivo.
- **FK hacia `Usuarios`**: `Restrict`, igual que toda otra FK hacia `Usuarios` del proyecto (`AppDbContext.cs:94`, comentario "Restrict porque Producto/Usuario usan baja lógica").
- **Migración de firma en dos fases**: la Task 5 agrega el nuevo `Verificar(ICurrentSession, string)` como *overload* de `IAuthorizationService`, conviviendo temporalmente con el `Verificar(RolUsuario?, string)` viejo y con `TienePermiso`. Los 96 call sites migran incrementalmente (Tasks 6a-6d) sin romper la compilación en ningún punto intermedio. La Task 7 elimina los dos miembros viejos de la interfaz, una vez que Program.cs (único consumidor restante de `TienePermiso`) también se migra. Esto es lo que permite que la suite quede **verde al final de cada task**, incluidas las de migración mecánica — es una decisión de secuenciación de este plan, no algo que pida el spec explícitamente, documentada acá porque de otro modo la migración de 96 call sites sería un único commit gigante o rompería la compilación a mitad de camino.
- **`IProveedorPermisos` es Singleton pero `IPermisoUsuarioRepository` es Scoped** (usa `AppDbContext`). Un Singleton no puede inyectar un Scoped por constructor sin crear una dependencia cautiva. La implementación (Task 3) resuelve esto con `IServiceScopeFactory` y un scope propio por operación — mismo patrón que `BackupProgramadoService`/`DisparadorBackupManual` ya usan en este repo para el mismo problema. Esto tampoco está en el spec explícitamente: el spec describe el comportamiento (cache + repo) pero no resuelve el lifetime mismatch; este plan lo resuelve con el patrón ya establecido en el código.
- **Nunca duplicar el SQL de backfill** entre la migración y su test: vive en una constante compartida (`PermisoUsuarioBackfillSql`, Task 1) referenciada por ambos.

## File Structure

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Domain/Entities/PermisoUsuario.cs` | Entidad de la tabla `PermisosUsuario` |
| `src/StockApp.Infrastructure/Migrations/PermisoUsuarioBackfillSql.cs` | Constante compartida con el SQL de backfill (migración + test) |
| `src/StockApp.Infrastructure/Migrations/<timestamp>_AgregaPermisoUsuario.cs` | Migración EF: tabla + índice único + backfill |
| `src/StockApp.Application/Interfaces/IPermisoUsuarioRepository.cs` | Contrato de persistencia de permisos |
| `src/StockApp.Infrastructure/Repositories/PermisoUsuarioRepository.cs` | Implementación EF del repositorio |
| `src/StockApp.Application/Authorization/IProveedorPermisos.cs` | Contrato de resolución cacheada |
| `src/StockApp.Application/Authorization/ProveedorPermisosEnMemoria.cs` | Implementación singleton con cache + scope-per-operación |
| `src/StockApp.Application/Interfaces/ICurrentSession.cs` | Modificar: `PermisosActuales` + `EstablecerPermisos` |
| `src/StockApp.Api/Auth/HttpCurrentSession.cs` | Modificar: implementa los miembros nuevos |
| `src/StockApp.ApiClient/ApiSession.cs` | Modificar: implementa los miembros nuevos |
| `src/StockApp.Application/Authorization/Permisos.cs` | Sin cambios de contenido — consumido por los nuevos campos de `AuthorizationService` |
| `src/StockApp.Application/Authorization/AuthorizationService.cs` | Modificar: `PermisosEstructuralesAdmin`, `PermisosConfigurables`, `PermisosInicialesOperador`, nuevo overload de `Verificar`, luego limpieza de miembros viejos |
| `src/StockApp.Application/Authorization/IAuthorizationService.cs` | Modificar: agrega overload, luego elimina los viejos |
| 21 archivos de `StockApp.Application/*` | Modificar: migrar los 95 call sites de `_auth.Verificar(...)` (Tasks 6a-6d, enumerados ahí) |
| `src/StockApp.Api/Endpoints/BackupsEndpoints.cs` | Modificar: migrar el call site restante (Task 6d) |
| `src/StockApp.Api/Auth/PermisoRequirement.cs` | `IAuthorizationRequirement` nuevo |
| `src/StockApp.Api/Auth/PermisoAuthorizationHandler.cs` | Handler nuevo, reemplaza `RequireClaim` |
| `src/StockApp.Api/Program.cs` | Modificar: registro DI de `IPermisoUsuarioRepository`/`IProveedorPermisos` (Task 3), bloque de policies (Task 7), middleware (Task 8) |
| `tests/StockApp.Api.Tests/Auth/PermisosEndpointGuardTests.cs` | Test guardián de endpoints (Task 7) |
| `tests/StockApp.Api.Tests/Auth/PoblarPermisosMiddlewareTests.cs` | Tests del middleware (Task 8) |
| `src/StockApp.Api/Endpoints/AuthEndpoints.cs` | Modificar: `GET /auth/permisos` (Task 9) |
| `src/StockApp.Application/Auth/IAuthService.cs` | Modificar: `ObtenerPermisosPropiosAsync` (Task 9) |
| `src/StockApp.ApiClient/AuthApiClient.cs` | Modificar: implementación + refresco tras login (Task 9) |
| `src/StockApp.Presentation/Services/RefrescoPermisos.cs` | Helper compartido de "mejor esfuerzo" (Task 9), consumido por Tasks 13, 14 y 15 |
| `src/StockApp.Api/Endpoints/UsuariosEndpoints.cs` | Modificar: `GET/PUT /usuarios/{id}/permisos` (Task 10) |
| `src/StockApp.Application/Auth/IUsuarioService.cs` / `UsuarioApiClient.cs` | Modificar: métodos de permisos del cliente (Task 10) |
| `src/StockApp.Domain/Enums/AccionAuditada.cs` | Modificar: `ModificacionPermisosUsuario = 51` (Task 10) |
| `src/StockApp.Application/Auth/UsuarioService.cs` | Modificar: `AltaUsuarioAsync` siembra `PermisosInicialesOperador` (Task 11) |
| `src/StockApp.Presentation/ViewModels/Administracion/UsuariosAdminViewModel.cs` | Nuevo VM (Task 12) |
| `src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml(.cs)` | Nueva View (Task 12) |
| `src/StockApp.Presentation/ViewModels/Administracion/PanelPermisosViewModel.cs` | Nuevo VM del panel de permisos (Task 13) |
| `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` | Modificar: propiedades `Puede*` (Task 14) |
| `src/StockApp.Presentation/Views/ShellMainView.axaml` / `InicioView.axaml` | Modificar: 16 bindings `EsAdmin` → `Puede*` (Task 14) |
| `src/StockApp.ApiClient/AuthTokenHandler.cs` | Modificar: rama 403 → `AccesoRevocado` (Task 15) |
| `src/StockApp.ApiClient/ApiSession.cs` | Modificar: evento `AccesoRevocado` (Task 15) |
| `src/StockApp.Presentation/App.axaml.cs` | Modificar: cablea `AccesoRevocado` (Task 15) |

---

### Task 1: Entidad `PermisoUsuario`, migración con backfill determinista

**Files:**
- Create: `src/StockApp.Domain/Entities/PermisoUsuario.cs`
- Create: `src/StockApp.Infrastructure/Migrations/PermisoUsuarioBackfillSql.cs`
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs` (DbSet + `OnModelCreating`)
- Create: migración EF `AgregaPermisoUsuario`
- Test: `tests/StockApp.Infrastructure.Tests/Entidades/PermisoUsuarioBackfillTests.cs`

**Interfaces:**
- Produces: `PermisoUsuario` (props `Id`, `UsuarioId`, `Permiso`, `Usuario`); `DbSet<PermisoUsuario> AppDbContext.PermisosUsuario`; `PermisoUsuarioBackfillSql.InsertarPermisosIniciales` (const string).

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Infrastructure.Tests/Entidades/PermisoUsuarioBackfillTests.cs`. La migración ya corrió sobre una base vacía cuando el fixture arrancó el contenedor (no hay usuarios todavía), así que este test siembra un Operador y un Admin y **re-ejecuta la misma constante SQL** que usa la migración — verifica el comportamiento del backfill sin necesitar cirugía sobre el historial de migraciones de EF (ver Global Constraints, "Nunca duplicar el SQL de backfill"):

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Migrations;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Entidades;

public class PermisoUsuarioBackfillTests : PostgresRepositoryTestBase
{
    public PermisoUsuarioBackfillTests(PostgresFixture fixture) : base(fixture) { }

    private static Usuario NuevoUsuario(string nombre, RolUsuario rol) => new()
    {
        NombreUsuario  = nombre,
        HashContrasena = "hash-de-prueba",
        Rol            = rol,
        Activo         = true,
        FechaAlta      = DateTime.UtcNow,
    };

    [Fact]
    public async Task Backfill_OperadorExistente_RecibeExactamenteLos9PermisosDeAccionesOperador()
    {
        var operador = NuevoUsuario("operador.backfill", RolUsuario.Operador);
        Context.Usuarios.Add(operador);
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(PermisoUsuarioBackfillSql.InsertarPermisosIniciales);

        var permisos = await Context.PermisosUsuario
            .Where(p => p.UsuarioId == operador.Id)
            .Select(p => p.Permiso)
            .ToListAsync();

        Assert.Equal(9, permisos.Count);
        Assert.Contains("catalogo.productos", permisos);
        Assert.Contains("movimientos.registrar", permisos);
        Assert.Contains("stock.recalcular", permisos);
        Assert.Contains("finanzas.ver", permisos);
        Assert.Contains("finanzas.maestros", permisos);
        Assert.Contains("finanzas.gastos", permisos);
        Assert.Contains("finanzas.pagos", permisos);
        Assert.Contains("finanzas.ingresos", permisos);
        Assert.Contains("tareas.gestionar", permisos);
    }

    [Fact]
    public async Task Backfill_OperadorExistente_NoIncluyeVerReportesNiGestionarTablasMaestras()
    {
        var operador = NuevoUsuario("operador.sinreportes", RolUsuario.Operador);
        Context.Usuarios.Add(operador);
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(PermisoUsuarioBackfillSql.InsertarPermisosIniciales);

        var permisos = await Context.PermisosUsuario
            .Where(p => p.UsuarioId == operador.Id)
            .Select(p => p.Permiso)
            .ToListAsync();

        Assert.DoesNotContain("reportes.ver", permisos);
        Assert.DoesNotContain("catalogo.maestras", permisos);
    }

    [Fact]
    public async Task Backfill_Admin_NoRecibeNingunaFila()
    {
        var admin = NuevoUsuario("admin.backfill", RolUsuario.Admin);
        Context.Usuarios.Add(admin);
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(PermisoUsuarioBackfillSql.InsertarPermisosIniciales);

        var permisos = await Context.PermisosUsuario.Where(p => p.UsuarioId == admin.Id).ToListAsync();

        Assert.Empty(permisos);
    }

    [Fact]
    public async Task IndiceUnico_UsuarioIdPermiso_RechazaDuplicado()
    {
        var operador = NuevoUsuario("operador.duplicado", RolUsuario.Operador);
        Context.Usuarios.Add(operador);
        await Context.SaveChangesAsync();

        Context.PermisosUsuario.Add(new PermisoUsuario { UsuarioId = operador.Id, Permiso = "finanzas.ver" });
        await Context.SaveChangesAsync();

        Context.PermisosUsuario.Add(new PermisoUsuario { UsuarioId = operador.Id, Permiso = "finanzas.ver" });

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());
    }

    [Fact]
    public async Task ReejecutarBackfill_EsIdempotente_NoDuplicaFilas()
    {
        var operador = NuevoUsuario("operador.idempotente", RolUsuario.Operador);
        Context.Usuarios.Add(operador);
        await Context.SaveChangesAsync();

        await Context.Database.ExecuteSqlRawAsync(PermisoUsuarioBackfillSql.InsertarPermisosIniciales);
        await Context.Database.ExecuteSqlRawAsync(PermisoUsuarioBackfillSql.InsertarPermisosIniciales);

        var cantidad = await Context.PermisosUsuario.CountAsync(p => p.UsuarioId == operador.Id);

        Assert.Equal(9, cantidad);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~PermisoUsuarioBackfillTests`
Expected: FALLA de compilación — `PermisoUsuario`, `PermisoUsuarioBackfillSql` y `Context.PermisosUsuario` no existen.

- [ ] **Step 3: Crear la entidad**

`src/StockApp.Domain/Entities/PermisoUsuario.cs`:

```csharp
namespace StockApp.Domain.Entities;

/// <summary>
/// Un permiso configurable concedido a un usuario Operador. VERDAD ÚNICA de los permisos
/// configurables (spec 2026-08-10, decisión 3): resolver = SELECT, sin merge ni overrides.
/// Sin fila para un (UsuarioId, Permiso) = ese permiso no está concedido (fail-closed).
/// Nunca existen filas para los 4 permisos estructurales (GestionarUsuarios, ImportarPlanillas,
/// GestionarDiagnostico, AdministrarTareas) — esos nunca se resuelven contra esta tabla.
/// </summary>
public class PermisoUsuario
{
    public int Id { get; set; }

    /// <summary>FK a Usuarios.Id, Restrict (mismo criterio que toda otra FK hacia Usuarios:
    /// la baja es lógica, nunca DELETE físico, así que no hay cascada que propagar).</summary>
    public int UsuarioId { get; set; }

    /// <summary>Uno de los 11 permisos configurables de Permisos.cs (ej. "finanzas.ver").</summary>
    public string Permiso { get; set; } = string.Empty;

    public Usuario? Usuario { get; set; }
}
```

- [ ] **Step 4: Crear la constante compartida del backfill**

`src/StockApp.Infrastructure/Migrations/PermisoUsuarioBackfillSql.cs`:

```csharp
namespace StockApp.Infrastructure.Migrations;

/// <summary>
/// SQL de backfill de PermisosUsuario para Operadores preexistentes, extraído a una constante
/// compartida entre la migración (AgregaPermisoUsuario.Up) y el test de Infrastructure
/// (PermisoUsuarioBackfillTests) — evita que ambos textos diverjan con el tiempo. Orden y
/// contenido: exactamente los 9 permisos que hoy tiene AuthorizationService.PermisosInicialesOperador
/// (antes AccionesOperador), en el mismo orden textual del archivo — no el orden de iteración
/// de un HashSet, que no está garantizado (mismo criterio que la corrección aplicada al backfill
/// de LotesImportacion, commit af4321b). VerReportes y GestionarTablasMaestras quedan afuera
/// a propósito: hoy ningún Operador los tiene.
/// </summary>
public static class PermisoUsuarioBackfillSql
{
    public const string InsertarPermisosIniciales =
        """
        INSERT INTO "PermisosUsuario" ("UsuarioId", "Permiso")
        SELECT u."Id", p.permiso
        FROM "Usuarios" u
        CROSS JOIN (VALUES
            ('catalogo.productos'), ('movimientos.registrar'), ('stock.recalcular'),
            ('finanzas.ver'), ('finanzas.maestros'), ('finanzas.gastos'),
            ('finanzas.pagos'), ('finanzas.ingresos'), ('tareas.gestionar')
        ) AS p(permiso)
        WHERE u."Rol" = 1
        ON CONFLICT ("UsuarioId", "Permiso") DO NOTHING;
        """;
}
```

- [ ] **Step 5: Agregar el DbSet y el mapeo en AppDbContext**

En `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`, junto al resto de los `DbSet<>` (después de `LotesImportacion`):

```csharp
    public DbSet<PermisoUsuario> PermisosUsuario => Set<PermisoUsuario>();
```

Y en `OnModelCreating`, al final del método, justo antes del cierre de la clase:

```csharp
        // ── Permisos por operador (spec 2026-08-10) ───────────────────────────
        // UNIQUE (UsuarioId, Permiso): un mismo permiso no puede estar duplicado para el mismo
        // usuario. Restrict hacia Usuarios, mismo criterio que el resto del modelo (baja lógica,
        // nunca DELETE físico). Sin fila = sin ese permiso (fail-closed, decisión 3 del spec).
        modelBuilder.Entity<PermisoUsuario>(e =>
        {
            e.Property(p => p.Permiso).IsRequired();
            e.HasIndex(p => new { p.UsuarioId, p.Permiso }).IsUnique();
            e.HasOne(p => p.Usuario).WithMany()
                .HasForeignKey(p => p.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 6: Generar la migración**

Run: `dotnet ef migrations add AgregaPermisoUsuario --project src/StockApp.Infrastructure --startup-project src/StockApp.Api`

Después, editar el `Up()` de la migración generada para agregar al final el backfill (usando la constante del Step 4, `using StockApp.Infrastructure.Migrations;` ya implícito por estar en el mismo namespace):

```csharp
            migrationBuilder.Sql(PermisoUsuarioBackfillSql.InsertarPermisosIniciales);
```

Verificar que el nombre de tabla que generó EF sea exactamente `"PermisosUsuario"` (coincide con el `INSERT INTO` de la constante porque ambos derivan del mismo nombre de `DbSet`). Si EF generó otro nombre, es un error de nomenclatura — corregir el `DbSet` para que sea `PermisosUsuario`, no ajustar la constante.

- [ ] **Step 7: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~PermisoUsuarioBackfillTests`
Expected: 5 tests PASS.

- [ ] **Step 8: Correr la suite completa de Infrastructure para descartar regresiones**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: todo verde (línea base 317/317 más los 5 nuevos). Si estás en un worktree, los ~16 rojos de fixtures `.ods` son falsos negativos preexistentes — no son tuyos (ver discovery previa: worktrees no copian fixtures gitignored).

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Domain/Entities/PermisoUsuario.cs \
        src/StockApp.Infrastructure/Migrations/PermisoUsuarioBackfillSql.cs \
        src/StockApp.Infrastructure/Migrations/ \
        src/StockApp.Infrastructure/Persistence/AppDbContext.cs \
        tests/StockApp.Infrastructure.Tests/Entidades/PermisoUsuarioBackfillTests.cs
git commit -m "feat(permisos): entidad PermisoUsuario con migracion y backfill determinista"
```

---

### Task 2: `IPermisoUsuarioRepository` + implementación EF

**Files:**
- Create: `src/StockApp.Application/Interfaces/IPermisoUsuarioRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/PermisoUsuarioRepository.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/PermisoUsuarioRepositoryTests.cs`

**Interfaces:**
- Consumes: `PermisoUsuario` (Task 1, entidad); `AppDbContext.PermisosUsuario` (Task 1).
- Produces: `IPermisoUsuarioRepository.ObtenerPermisosAsync(int usuarioId)` → `Task<IReadOnlySet<string>>`; `.ReemplazarPermisosAsync(int usuarioId, IReadOnlyCollection<string> permisos)` → `Task`.

- [ ] **Step 1: Escribir el test que falla**

`tests/StockApp.Infrastructure.Tests/Repositories/PermisoUsuarioRepositoryTests.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class PermisoUsuarioRepositoryTests : PostgresRepositoryTestBase
{
    public PermisoUsuarioRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private PermisoUsuarioRepository Crear() => new(Context);

    private async Task<int> CrearOperadorAsync(string nombre)
    {
        var operador = new Usuario
        {
            NombreUsuario = nombre, HashContrasena = "hash", Rol = RolUsuario.Operador,
            Activo = true, FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(operador);
        await Context.SaveChangesAsync();
        return operador.Id;
    }

    [Fact]
    public async Task ObtenerPermisosAsync_SinFilas_DevuelveConjuntoVacio()
    {
        var usuarioId = await CrearOperadorAsync("operador.vacio");
        var repo = Crear();

        var permisos = await repo.ObtenerPermisosAsync(usuarioId);

        Assert.Empty(permisos);
    }

    [Fact]
    public async Task ReemplazarPermisosAsync_SinFilasPrevias_InsertaTodas()
    {
        var usuarioId = await CrearOperadorAsync("operador.alta");
        var repo = Crear();

        await repo.ReemplazarPermisosAsync(usuarioId,
            new[] { Permisos.VerFinanzas, Permisos.GestionarProductos });

        var releidos = await new PermisoUsuarioRepository(Fixture.CrearContexto())
            .ObtenerPermisosAsync(usuarioId);
        Assert.Equal(2, releidos.Count);
        Assert.Contains(Permisos.VerFinanzas, releidos);
        Assert.Contains(Permisos.GestionarProductos, releidos);
    }

    [Fact]
    public async Task ReemplazarPermisosAsync_ConFilasPrevias_DejaSoloElSetNuevo()
    {
        var usuarioId = await CrearOperadorAsync("operador.reemplazo");
        var repo = Crear();
        await repo.ReemplazarPermisosAsync(usuarioId, new[] { Permisos.VerFinanzas, Permisos.GestionarTareas });

        await new PermisoUsuarioRepository(Fixture.CrearContexto())
            .ReemplazarPermisosAsync(usuarioId, new[] { Permisos.RecalcularStock });

        var final = await new PermisoUsuarioRepository(Fixture.CrearContexto()).ObtenerPermisosAsync(usuarioId);
        Assert.Single(final);
        Assert.Contains(Permisos.RecalcularStock, final);
    }

    [Fact]
    public async Task ReemplazarPermisosAsync_ConListaVacia_DejaAlUsuarioSinPermisos()
    {
        var usuarioId = await CrearOperadorAsync("operador.destildado");
        var repo = Crear();
        await repo.ReemplazarPermisosAsync(usuarioId, new[] { Permisos.VerFinanzas });

        await new PermisoUsuarioRepository(Fixture.CrearContexto())
            .ReemplazarPermisosAsync(usuarioId, Array.Empty<string>());

        var final = await new PermisoUsuarioRepository(Fixture.CrearContexto()).ObtenerPermisosAsync(usuarioId);
        Assert.Empty(final);
    }

    [Fact]
    public async Task DosUsuarios_TienenPermisosIndependientes()
    {
        var idA = await CrearOperadorAsync("operador.a");
        var idB = await CrearOperadorAsync("operador.b");
        var repo = Crear();

        await repo.ReemplazarPermisosAsync(idA, new[] { Permisos.VerFinanzas });
        await new PermisoUsuarioRepository(Fixture.CrearContexto())
            .ReemplazarPermisosAsync(idB, new[] { Permisos.GestionarProductos });

        var permisosA = await new PermisoUsuarioRepository(Fixture.CrearContexto()).ObtenerPermisosAsync(idA);
        var permisosB = await new PermisoUsuarioRepository(Fixture.CrearContexto()).ObtenerPermisosAsync(idB);
        Assert.DoesNotContain(Permisos.GestionarProductos, permisosA);
        Assert.DoesNotContain(Permisos.VerFinanzas, permisosB);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~PermisoUsuarioRepositoryTests`
Expected: FALLA de compilación — `IPermisoUsuarioRepository` y `PermisoUsuarioRepository` no existen.

- [ ] **Step 3: Crear el contrato**

`src/StockApp.Application/Interfaces/IPermisoUsuarioRepository.cs`:

```csharp
namespace StockApp.Application.Interfaces;

/// <summary>
/// Persistencia cruda de PermisoUsuario. Sin cache, sin conocer los 4 permisos estructurales —
/// esa lógica vive en AuthorizationService/IProveedorPermisos, más arriba en la pila. Un SELECT
/// sin filas devuelve un conjunto vacío, nunca null (fail-closed, spec decisión 3).
/// </summary>
public interface IPermisoUsuarioRepository
{
    /// <summary>Permisos configurables actuales del usuario. Vacío si no hay filas.</summary>
    Task<IReadOnlySet<string>> ObtenerPermisosAsync(int usuarioId);

    /// <summary>
    /// Reemplaza el set completo del usuario: borra las filas existentes e inserta las nuevas
    /// dentro de una única transacción. Una colección vacía deja al usuario sin permisos.
    /// </summary>
    Task ReemplazarPermisosAsync(int usuarioId, IReadOnlyCollection<string> permisos);
}
```

- [ ] **Step 4: Implementar el repositorio**

`src/StockApp.Infrastructure/Repositories/PermisoUsuarioRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class PermisoUsuarioRepository : IPermisoUsuarioRepository
{
    private readonly AppDbContext _ctx;

    public PermisoUsuarioRepository(AppDbContext ctx) => _ctx = ctx;

    public async Task<IReadOnlySet<string>> ObtenerPermisosAsync(int usuarioId)
    {
        var permisos = await _ctx.PermisosUsuario
            .Where(p => p.UsuarioId == usuarioId)
            .Select(p => p.Permiso)
            .ToListAsync();
        return new HashSet<string>(permisos);
    }

    public async Task ReemplazarPermisosAsync(int usuarioId, IReadOnlyCollection<string> permisos)
    {
        await using var transaccion = await _ctx.Database.BeginTransactionAsync();

        var existentes = await _ctx.PermisosUsuario
            .Where(p => p.UsuarioId == usuarioId)
            .ToListAsync();
        _ctx.PermisosUsuario.RemoveRange(existentes);

        foreach (var permiso in permisos)
            _ctx.PermisosUsuario.Add(new PermisoUsuario { UsuarioId = usuarioId, Permiso = permiso });

        await _ctx.SaveChangesAsync();
        await transaccion.CommitAsync();
    }
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~PermisoUsuarioRepositoryTests`
Expected: 5 tests PASS.

- [ ] **Step 6: Correr la suite completa de Infrastructure**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: todo verde (línea base 322/322 tras la Task 1, más los 5 nuevos = 327).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Interfaces/IPermisoUsuarioRepository.cs \
        src/StockApp.Infrastructure/Repositories/PermisoUsuarioRepository.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/PermisoUsuarioRepositoryTests.cs
git commit -m "feat(permisos): repositorio EF de PermisoUsuario con reemplazo transaccional"
```

---

### Task 3: `IProveedorPermisos` con cache en memoria

**Files:**
- Create: `src/StockApp.Application/Authorization/IProveedorPermisos.cs`
- Create: `src/StockApp.Application/Authorization/ProveedorPermisosEnMemoria.cs`
- Modify: `src/StockApp.Api/Program.cs` (registro DI, Step 6)
- Test: `tests/StockApp.Application.Tests/Authorization/ProveedorPermisosEnMemoriaTests.cs`

**Interfaces:**
- Consumes: `IPermisoUsuarioRepository` (Task 2): `ObtenerPermisosAsync(int)` → `Task<IReadOnlySet<string>>`, `ReemplazarPermisosAsync(int, IReadOnlyCollection<string>)` → `Task`.
- Produces: `IProveedorPermisos.ObtenerAsync(int usuarioId)` → `Task<IReadOnlySet<string>>`; `.GuardarAsync(int usuarioId, IReadOnlyCollection<string> permisos)` → `Task`; constructor `ProveedorPermisosEnMemoria(IServiceScopeFactory scopeFactory)`.

**Decisión de diseño (no está en el spec, resuelve un gap real):** `IProveedorPermisos` es Singleton (la cache tiene que sobrevivir entre requests) pero `IPermisoUsuarioRepository` es Scoped (usa `AppDbContext`). Inyectar un Scoped por constructor en un Singleton es una dependencia cautiva — el mismo `AppDbContext` (y su conexión) quedaría vivo para siempre, reusado entre requests de forma insegura. La solución es la que ya usa este repo para el mismo problema (`BackupProgramadoService`, `DisparadorBackupManual`): inyectar `IServiceScopeFactory` y crear un scope propio por operación.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Application.Tests/Authorization/ProveedorPermisosEnMemoriaTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using Xunit;

namespace StockApp.Application.Tests.Authorization;

public class ProveedorPermisosEnMemoriaTests
{
    private sealed class PermisoUsuarioRepositoryFake : IPermisoUsuarioRepository
    {
        public int LlamadasObtener { get; private set; }
        public int LlamadasReemplazar { get; private set; }
        public Dictionary<int, HashSet<string>> Datos { get; } = new();

        public Task<IReadOnlySet<string>> ObtenerPermisosAsync(int usuarioId)
        {
            LlamadasObtener++;
            var permisos = Datos.TryGetValue(usuarioId, out var p) ? p : new HashSet<string>();
            return Task.FromResult<IReadOnlySet<string>>(permisos);
        }

        public Task ReemplazarPermisosAsync(int usuarioId, IReadOnlyCollection<string> permisos)
        {
            LlamadasReemplazar++;
            Datos[usuarioId] = new HashSet<string>(permisos);
            return Task.CompletedTask;
        }
    }

    // Registrar el fake como Scoped vía factory que siempre devuelve LA MISMA instancia:
    // simula fielmente el lifetime real (Scoped) mientras permite contar llamadas a través
    // de scopes distintos, igual que un singleton de test — mismo patrón usado para verificar
    // wiring de DI real en vez de mockear IServiceScopeFactory a mano.
    private static (ProveedorPermisosEnMemoria Sut, PermisoUsuarioRepositoryFake Repo) Crear()
    {
        var repo = new PermisoUsuarioRepositoryFake();
        var services = new ServiceCollection();
        services.AddScoped<IPermisoUsuarioRepository>(_ => repo);
        var provider = services.BuildServiceProvider();
        var sut = new ProveedorPermisosEnMemoria(provider.GetRequiredService<IServiceScopeFactory>());
        return (sut, repo);
    }

    [Fact]
    public async Task ObtenerAsync_CacheMiss_DisparaUnSoloSelect()
    {
        var (sut, repo) = Crear();
        repo.Datos[7] = new HashSet<string> { Permisos.VerFinanzas };

        var permisos = await sut.ObtenerAsync(7);

        Assert.Equal(1, repo.LlamadasObtener);
        Assert.Contains(Permisos.VerFinanzas, permisos);
    }

    [Fact]
    public async Task ObtenerAsync_CacheHit_NoVuelveATocarElRepositorio()
    {
        var (sut, repo) = Crear();
        repo.Datos[7] = new HashSet<string> { Permisos.VerFinanzas };
        await sut.ObtenerAsync(7);

        await sut.ObtenerAsync(7);
        await sut.ObtenerAsync(7);

        Assert.Equal(1, repo.LlamadasObtener);
    }

    [Fact]
    public async Task ObtenerAsync_SinFilas_FailClosedDevuelveVacio()
    {
        var (sut, _) = Crear();

        var permisos = await sut.ObtenerAsync(999);

        Assert.Empty(permisos);
    }

    [Fact]
    public async Task GuardarAsync_PersisteEnElRepositorio()
    {
        var (sut, repo) = Crear();

        await sut.GuardarAsync(7, new[] { Permisos.GestionarProductos });

        Assert.Equal(1, repo.LlamadasReemplazar);
        Assert.Contains(Permisos.GestionarProductos, repo.Datos[7]);
    }

    [Fact]
    public async Task GuardarAsync_InvalidaLaCache_ElSiguienteObtenerAsyncReflejaLoNuevo()
    {
        var (sut, _) = Crear();
        await sut.ObtenerAsync(7); // puebla la cache con el estado vacío inicial

        await sut.GuardarAsync(7, new[] { Permisos.RecalcularStock });
        var releido = await sut.ObtenerAsync(7);

        Assert.Contains(Permisos.RecalcularStock, releido);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~ProveedorPermisosEnMemoriaTests`
Expected: FALLA de compilación — `IProveedorPermisos` y `ProveedorPermisosEnMemoria` no existen.

- [ ] **Step 3: Crear el contrato**

`src/StockApp.Application/Authorization/IProveedorPermisos.cs`:

```csharp
namespace StockApp.Application.Authorization;

/// <summary>
/// Resolución cacheada de los permisos configurables de un usuario (spec 2026-08-10). Misma
/// forma que IRevocadorTokens: una sola interfaz que junta lectura cacheada y escritura con
/// invalidación, en vez de separar en dos servicios. Componente de INFRAESTRUCTURA DE
/// RESOLUCIÓN (cache + repo), no de política: devuelve el SELECT crudo, sin Admin bypass y
/// sin conocer los 4 permisos estructurales — esa lógica de negocio vive en
/// AuthorizationService.Verificar, que nunca llama a esta interfaz directamente (lee un
/// resultado ya resuelto por el middleware). Nada viaja en el JWT: cada resolución consulta
/// el estado actual (de cache o de DB).
/// </summary>
public interface IProveedorPermisos
{
    /// <summary>Permisos configurables del usuario. Cache-first; SELECT contra
    /// PermisoUsuario solo en cache-miss.</summary>
    Task<IReadOnlySet<string>> ObtenerAsync(int usuarioId);

    /// <summary>Reemplaza el set completo del usuario e invalida su entrada de cache
    /// en la misma operación.</summary>
    Task GuardarAsync(int usuarioId, IReadOnlyCollection<string> permisos);
}
```

- [ ] **Step 4: Implementar el proveedor**

`src/StockApp.Application/Authorization/ProveedorPermisosEnMemoria.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Authorization;

/// <summary>
/// Implementación SINGLETON en memoria de proceso (mismo criterio que RevocadorTokensEnMemoria):
/// ConcurrentDictionary como cache, IPermisoUsuarioRepository (Scoped, usa AppDbContext) para el
/// SELECT/reemplazo en cache-miss o en GuardarAsync. Un Singleton no puede inyectar un Scoped por
/// constructor sin crear una dependencia cautiva (el mismo AppDbContext/conexión quedaría vivo
/// para siempre) — por eso se inyecta IServiceScopeFactory y se crea un scope propio por
/// operación, mismo patrón que BackupProgramadoService/DisparadorBackupManual ya usan en este
/// repo para el mismo problema de lifetime.
///
/// LIMITACIÓN ACEPTADA (spec, "Limitación conocida"): la cache es por PROCESO. Con más de una
/// instancia de API corriendo en paralelo, un cambio de permisos guardado en una no invalida la
/// cache de las otras. Hoy corre una sola instancia bajo systemd — misma limitación ya aceptada
/// en RevocadorTokensEnMemoria.
/// </summary>
public sealed class ProveedorPermisosEnMemoria : IProveedorPermisos
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConcurrentDictionary<int, IReadOnlySet<string>> _cache = new();

    public ProveedorPermisosEnMemoria(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public async Task<IReadOnlySet<string>> ObtenerAsync(int usuarioId)
    {
        if (_cache.TryGetValue(usuarioId, out var enCache))
            return enCache;

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPermisoUsuarioRepository>();
        var permisos = await repo.ObtenerPermisosAsync(usuarioId);

        _cache[usuarioId] = permisos;
        return permisos;
    }

    public async Task GuardarAsync(int usuarioId, IReadOnlyCollection<string> permisos)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPermisoUsuarioRepository>();
        await repo.ReemplazarPermisosAsync(usuarioId, permisos);

        // Invalidación = sobreescribir con el valor fresco, no un Remove(): un ObtenerAsync
        // concurrente que ya estaba en vuelo antes de este GuardarAsync no puede "revivir" un
        // valor viejo después, porque no hay ventana entre invalidar y repoblar.
        _cache[usuarioId] = new HashSet<string>(permisos);
    }
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~ProveedorPermisosEnMemoriaTests`
Expected: 6 tests PASS.

- [ ] **Step 6: Registrar en DI**

En `src/StockApp.Api/Program.cs`, después del bloque de registros de Usuarios (línea ~244, antes del bloque de Bootstrap), agregar:

```csharp
// Permisos por operador (spec 2026-08-10): IProveedorPermisos es Singleton (cache de proceso);
// IPermisoUsuarioRepository es Scoped (usa AppDbContext) — el proveedor resuelve el lifetime
// mismatch con su propio IServiceScopeFactory, no inyectando el repo directo.
builder.Services.AddScoped<IPermisoUsuarioRepository, PermisoUsuarioRepository>();
builder.Services.AddSingleton<IProveedorPermisos, ProveedorPermisosEnMemoria>();
```

Agregar los `using` que falten al tope de `Program.cs`: `StockApp.Infrastructure.Repositories` ya está importado; falta nada nuevo salvo que `StockApp.Application.Authorization` ya está importado (por `Permisos`/`AuthorizationService`).

- [ ] **Step 7: Correr la suite completa de Application**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: todo verde.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Application/Authorization/IProveedorPermisos.cs \
        src/StockApp.Application/Authorization/ProveedorPermisosEnMemoria.cs \
        src/StockApp.Api/Program.cs \
        tests/StockApp.Application.Tests/Authorization/ProveedorPermisosEnMemoriaTests.cs
git commit -m "feat(permisos): IProveedorPermisos con cache en memoria y scope-per-operacion"
```

---

### Task 4: `ICurrentSession.PermisosActuales` + `EstablecerPermisos`

**Files:**
- Modify: `src/StockApp.Application/Interfaces/ICurrentSession.cs`
- Modify: `src/StockApp.Api/Auth/HttpCurrentSession.cs`
- Modify: `src/StockApp.ApiClient/ApiSession.cs`
- Test: `tests/StockApp.Api.Tests/Auth/HttpCurrentSessionTests.cs` (nuevo — no existe en el repo)
- Test: `tests/StockApp.ApiClient.Tests/ApiSessionTests.cs` (agregar)

**Interfaces:**
- Produces: `ICurrentSession.PermisosActuales` → `IReadOnlySet<string>` (nunca null, vacío por default); `ICurrentSession.EstablecerPermisos(IReadOnlySet<string> permisos)`.

- [ ] **Step 1: Escribir los tests que fallan (ApiClient)**

Buscar si existe `tests/StockApp.ApiClient.Tests/ApiSessionTests.cs`; si no existe, crearlo con este contenido completo (si existe, agregar los `[Fact]` de abajo a la clase existente y no duplicar el resto):

```csharp
using StockApp.ApiClient;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient.Tests;

public class ApiSessionTests
{
    [Fact]
    public void PermisosActuales_SinSesion_DevuelveConjuntoVacio()
    {
        var session = new ApiSession();

        Assert.Empty(session.PermisosActuales);
    }

    [Fact]
    public void EstablecerPermisos_PueblaPermisosActuales()
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "operador", RolUsuario.Operador, null), "token123");

        session.EstablecerPermisos(new HashSet<string> { "finanzas.ver" });

        Assert.Contains("finanzas.ver", session.PermisosActuales);
    }

    [Fact]
    public void CerrarSesion_LimpiaTambienLosPermisos()
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "operador", RolUsuario.Operador, null), "token123");
        session.EstablecerPermisos(new HashSet<string> { "finanzas.ver" });

        session.CerrarSesion();

        Assert.Empty(session.PermisosActuales);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter FullyQualifiedName~ApiSessionTests`
Expected: FALLA de compilación — `PermisosActuales` y `EstablecerPermisos` no existen en `ApiSession`.

- [ ] **Step 3: Ampliar el contrato `ICurrentSession`**

En `src/StockApp.Application/Interfaces/ICurrentSession.cs`, agregar después de `RolActual`:

```csharp
    /// <summary>
    /// Permisos configurables efectivos del usuario actual (spec 2026-08-10). Vacío si no hay
    /// sesión o si el usuario no tiene ninguno concedido — nunca null. Para Admin, quien la
    /// puebla (el middleware del lado API, o ApiSession del lado desktop) puede optar por
    /// dejarlo vacío igual: AuthorizationService.Verificar nunca consulta este set para Admin,
    /// así que su contenido es irrelevante en ese caso.
    /// </summary>
    IReadOnlySet<string> PermisosActuales { get; }

    /// <summary>Puebla PermisosActuales. Llamado una vez por request (HttpCurrentSession, vía el
    /// middleware nuevo) o tras login/refresh (ApiSession).</summary>
    void EstablecerPermisos(IReadOnlySet<string> permisos);
```

- [ ] **Step 4: Implementar en `ApiSession`**

En `src/StockApp.ApiClient/ApiSession.cs`, agregar el campo privado (junto a `_sesionActual`/`_token`), la propiedad y el método:

```csharp
    private IReadOnlySet<string> _permisos = new HashSet<string>();
```

```csharp
    public IReadOnlySet<string> PermisosActuales { get { lock (_lock) return _permisos; } }

    public void EstablecerPermisos(IReadOnlySet<string> permisos)
    {
        ArgumentNullException.ThrowIfNull(permisos);
        lock (_lock) _permisos = permisos;
    }
```

Y en `CerrarSesion()`, agregar la limpieza:

```csharp
    public void CerrarSesion()
    {
        lock (_lock)
        {
            _sesionActual = null;
            _token        = null;
            _permisos     = new HashSet<string>();
        }
    }
```

- [ ] **Step 5: Correr los tests de ApiClient y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter FullyQualifiedName~ApiSessionTests`
Expected: 3 tests PASS.

- [ ] **Step 6: Escribir los tests que fallan (Api)**

`tests/StockApp.Api.Tests/Auth/HttpCurrentSessionTests.cs` no existe todavía en el repo — crearlo con este contenido:

```csharp
using Microsoft.AspNetCore.Http;
using StockApp.Api.Auth;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class HttpCurrentSessionTests
{
    private static HttpCurrentSession Crear()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        return new HttpCurrentSession(accessor);
    }

    [Fact]
    public void PermisosActuales_AntesDePoblar_DevuelveConjuntoVacio()
    {
        var session = Crear();

        Assert.Empty(session.PermisosActuales);
    }

    [Fact]
    public void EstablecerPermisos_PueblaPermisosActuales()
    {
        var session = Crear();

        session.EstablecerPermisos(new HashSet<string> { "finanzas.ver" });

        Assert.Contains("finanzas.ver", session.PermisosActuales);
    }
}
```

- [ ] **Step 7: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~HttpCurrentSessionTests`
Expected: FALLA de compilación — `PermisosActuales`/`EstablecerPermisos` no existen en `HttpCurrentSession`.

- [ ] **Step 8: Implementar en `HttpCurrentSession`**

En `src/StockApp.Api/Auth/HttpCurrentSession.cs`, agregar el campo, la propiedad y el método (después del campo `_accessor`):

```csharp
    private IReadOnlySet<string> _permisos = new HashSet<string>();
```

```csharp
    public IReadOnlySet<string> PermisosActuales => _permisos;

    public void EstablecerPermisos(IReadOnlySet<string> permisos)
    {
        ArgumentNullException.ThrowIfNull(permisos);
        _permisos = permisos;
    }
```

Nota: a diferencia de `ApiSession`, `HttpCurrentSession` es Scoped (una instancia por request HTTP, sin concurrencia interna posible dentro del mismo request) — no necesita el `lock` que sí tiene `ApiSession` (Singleton, compartida entre hilos de la UI de Avalonia).

- [ ] **Step 9: Correr los tests de Api y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~HttpCurrentSessionTests`
Expected: 2 tests PASS.

- [ ] **Step 10: Correr la suite completa de Api y ApiClient**

Run: `dotnet test tests/StockApp.Api.Tests && dotnet test tests/StockApp.ApiClient.Tests`
Expected: todo verde. Nota: al agregar dos miembros a `ICurrentSession`, cualquier implementación manual de la interfaz en tests (fakes escritos a mano, no Moq) puede fallar a compilar si no implementa los miembros nuevos — si aparece un error de compilación de un fake de `ICurrentSession` en algún test de Api/ApiClient, agregar `PermisosActuales => new HashSet<string>();` y `EstablecerPermisos(IReadOnlySet<string> _) { }` (no-op) a ese fake antes de continuar.

- [ ] **Step 11: Commit**

```bash
git add src/StockApp.Application/Interfaces/ICurrentSession.cs \
        src/StockApp.Api/Auth/HttpCurrentSession.cs \
        src/StockApp.ApiClient/ApiSession.cs \
        tests/StockApp.Api.Tests/Auth/HttpCurrentSessionTests.cs \
        tests/StockApp.ApiClient.Tests/ApiSessionTests.cs
git commit -m "feat(permisos): ICurrentSession.PermisosActuales en HttpCurrentSession y ApiSession"
```

---

### Task 5: `AuthorizationService.Verificar(ICurrentSession, string)` — nuevo overload

**Files:**
- Modify: `src/StockApp.Application/Authorization/IAuthorizationService.cs`
- Modify: `src/StockApp.Application/Authorization/AuthorizationService.cs`
- Test: `tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs`

**Interfaces:**
- Consumes: `ICurrentSession.EstaAutenticado`, `.RolActual`, `.PermisosActuales` (Task 4).
- Produces: `AuthorizationService.PermisosEstructuralesAdmin` → `IReadOnlySet<string>` (static); `AuthorizationService.PermisosConfigurables` → `IReadOnlyList<string>` (static); `AuthorizationService.PermisosInicialesOperador` → `IReadOnlyList<string>` (static, orden fijo); `IAuthorizationService.Verificar(ICurrentSession sesion, string accion)` (nuevo overload — el viejo `Verificar(RolUsuario?, string)` y `TienePermiso` se mantienen sin cambios en esta task, se eliminan en la Task 7).

- [ ] **Step 1: Escribir los tests que fallan**

Agregar a `tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs` (al final de la clase existente, antes del cierre) los tests del nuevo overload. El archivo hoy solo importa `StockApp.Application.Authorization`, `StockApp.Domain.Enums` y `Xunit` — agregar además `using StockApp.Application.Auth;` (para `UsuarioSesion`), `using StockApp.Application.Interfaces;` (para `ICurrentSession`) y `using StockApp.Domain.Entities;` (para `Usuario`, usado en la firma de `IniciarSesion` del fake) al tope:

```csharp
    // ── Verificar(ICurrentSession, string) — nuevo overload (spec 2026-08-10) ──────────────

    private sealed class SesionFake : ICurrentSession
    {
        public bool EstaAutenticado { get; set; } = true;
        public UsuarioSesion? UsuarioActual { get; set; }
        public RolUsuario? RolActual { get; set; }
        public IReadOnlySet<string> PermisosActuales { get; set; } = new HashSet<string>();
        public void IniciarSesion(Usuario usuario) => throw new NotSupportedException();
        public void CerrarSesion() => throw new NotSupportedException();
        public void EstablecerPermisos(IReadOnlySet<string> permisos) => PermisosActuales = permisos;
    }

    [Fact]
    public void VerificarConSesion_SinAutenticar_LanzaUnauthorized()
    {
        var sesion = new SesionFake { EstaAutenticado = false };

        Assert.Throws<UnauthorizedAccessException>(
            () => _svc.Verificar(sesion, Permisos.VerFinanzas));
    }

    [Fact]
    public void VerificarConSesion_Admin_PasaSiempre_SinConsultarPermisosActuales()
    {
        // PermisosActuales queda deliberadamente null-like (vacío): si Verificar lo consultara
        // para Admin, este test lo detectaría por Assert.True en vez de simplemente no lanzar.
        var sesion = new SesionFake { RolActual = RolUsuario.Admin, PermisosActuales = new HashSet<string>() };

        var ex = Record.Exception(() => _svc.Verificar(sesion, Permisos.GestionarUsuarios));

        Assert.Null(ex);
    }

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.ImportarPlanillas)]
    [InlineData(Permisos.GestionarDiagnostico)]
    [InlineData(Permisos.AdministrarTareas)]
    public void VerificarConSesion_Operador_LosCuatroEstructurales_RechazanSiempre(string permisoEstructural)
    {
        var sesion = new SesionFake { RolActual = RolUsuario.Operador, PermisosActuales = new HashSet<string>() };

        Assert.Throws<UnauthorizedAccessException>(() => _svc.Verificar(sesion, permisoEstructural));
    }

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.ImportarPlanillas)]
    [InlineData(Permisos.GestionarDiagnostico)]
    [InlineData(Permisos.AdministrarTareas)]
    public void VerificarConSesion_SesionEnvenenada_LosCuatroEstructuralesRechazanIgual(string permisoEstructural)
    {
        // El test más importante de esta clase: aunque PermisosActuales CONTENGA el permiso
        // estructural (una fila colada por error, o un bug futuro en PUT /usuarios/{id}/permisos),
        // Verificar tiene que rechazar igual. Este es el corte que el spec marca como "el punto
        // de falla más peligroso" — si se invirtiera el orden, un Operador con una fila colada
        // podría auto-otorgarse GestionarUsuarios y desde ahí cualquier otro permiso.
        var sesionEnvenenada = new SesionFake
        {
            RolActual = RolUsuario.Operador,
            PermisosActuales = new HashSet<string> { permisoEstructural },
        };

        Assert.Throws<UnauthorizedAccessException>(() => _svc.Verificar(sesionEnvenenada, permisoEstructural));
    }

    [Fact]
    public void VerificarConSesion_Operador_ConElPermisoEnPermisosActuales_Pasa()
    {
        var sesion = new SesionFake
        {
            RolActual = RolUsuario.Operador,
            PermisosActuales = new HashSet<string> { Permisos.VerFinanzas },
        };

        var ex = Record.Exception(() => _svc.Verificar(sesion, Permisos.VerFinanzas));

        Assert.Null(ex);
    }

    [Fact]
    public void VerificarConSesion_Operador_SinElPermisoEnPermisosActuales_Lanza()
    {
        var sesion = new SesionFake
        {
            RolActual = RolUsuario.Operador,
            PermisosActuales = new HashSet<string> { Permisos.GestionarProductos },
        };

        Assert.Throws<UnauthorizedAccessException>(() => _svc.Verificar(sesion, Permisos.VerFinanzas));
    }

    [Fact]
    public void PermisosEstructuralesAdmin_ContieneExactamenteLosCuatroDocumentados()
    {
        Assert.Equal(4, AuthorizationService.PermisosEstructuralesAdmin.Count);
        Assert.Contains(Permisos.GestionarUsuarios, AuthorizationService.PermisosEstructuralesAdmin);
        Assert.Contains(Permisos.ImportarPlanillas, AuthorizationService.PermisosEstructuralesAdmin);
        Assert.Contains(Permisos.GestionarDiagnostico, AuthorizationService.PermisosEstructuralesAdmin);
        Assert.Contains(Permisos.AdministrarTareas, AuthorizationService.PermisosEstructuralesAdmin);
    }

    [Fact]
    public void PermisosConfigurables_TieneLos11RestantesYNoIntersecaConLosEstructurales()
    {
        Assert.Equal(11, AuthorizationService.PermisosConfigurables.Count);
        foreach (var permiso in AuthorizationService.PermisosConfigurables)
            Assert.DoesNotContain(permiso, AuthorizationService.PermisosEstructuralesAdmin);
    }

    [Fact]
    public void PermisosInicialesOperador_TieneExactamenteLos9DeAccionesOperadorEnOrden()
    {
        Assert.Equal(new[]
        {
            Permisos.GestionarProductos,
            Permisos.RegistrarMovimientos,
            Permisos.RecalcularStock,
            Permisos.VerFinanzas,
            Permisos.GestionarMaestrosFinanzas,
            Permisos.RegistrarGastos,
            Permisos.RegistrarPagos,
            Permisos.RegistrarIngresos,
            Permisos.GestionarTareas,
        }, AuthorizationService.PermisosInicialesOperador);
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~AuthorizationServiceTests`
Expected: FALLA de compilación — el overload `Verificar(ICurrentSession, string)`, `PermisosEstructuralesAdmin`, `PermisosConfigurables` y `PermisosInicialesOperador` no existen.

- [ ] **Step 3: Ampliar `IAuthorizationService`**

En `src/StockApp.Application/Authorization/IAuthorizationService.cs`, agregar el nuevo overload sin tocar los miembros existentes (se eliminan en la Task 7, cuando el último consumidor — `Program.cs` — ya esté migrado):

```csharp
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;

namespace StockApp.Application.Authorization;

/// <summary>
/// Guard de autorización. Cada servicio de Application llama a <see cref="Verificar"/> al
/// inicio de los métodos que requieren permiso.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Verifica que <paramref name="rolActual"/> puede ejecutar <paramref name="accion"/>.
    /// OBSOLETO (spec 2026-08-10): reemplazado por el overload que recibe ICurrentSession
    /// completo. Se mantiene temporalmente mientras los ~96 call sites migran (Tasks 6a-6d);
    /// se elimina en la Task 7 junto con TienePermiso.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Si el rol no tiene permiso o no hay sesión.</exception>
    void Verificar(RolUsuario? rolActual, string accion);

    /// <summary>
    /// Verifica que la sesión completa (rol + permisos configurables ya resueltos por el
    /// middleware, spec 2026-08-10) puede ejecutar <paramref name="accion"/>. SINCRÓNICO:
    /// no hace ningún SELECT, lee sesion.PermisosActuales, ya poblado antes de que cualquier
    /// servicio de Application se ejecute.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Sin sesión, o sin el permiso requerido.</exception>
    void Verificar(ICurrentSession sesion, string accion);

    /// <summary>
    /// Igual que <see cref="Verificar(RolUsuario?, string)"/> pero sin lanzar. OBSOLETO:
    /// único consumidor es Program.cs (deriva rolesPermitidos al arrancar); desaparece en la
    /// Task 7 junto con ese código.
    /// </summary>
    bool TienePermiso(RolUsuario rol, string accion);
}
```

- [ ] **Step 4: Implementar en `AuthorizationService`**

Reemplazar el contenido completo de `src/StockApp.Application/Authorization/AuthorizationService.cs`:

```csharp
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;

namespace StockApp.Application.Authorization;

/// <summary>
/// Implementación de <see cref="IAuthorizationService"/>. Admin tiene acceso a todo. Para
/// Operador, cuatro permisos son estructuralmente Admin-only (nunca se resuelven contra
/// PermisoUsuario) y los 11 restantes se resuelven contra ICurrentSession.PermisosActuales,
/// ya poblado por el middleware de la Task 8 antes de que cualquier servicio de Application
/// se ejecute (spec 2026-08-10).
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    /// <summary>
    /// Los 4 permisos que NUNCA se resuelven contra PermisoUsuario: Admin los tiene siempre,
    /// Operador nunca, sin consultar la tabla ni la cache. Punto de falla más peligroso del
    /// diseño (spec, "Riesgos") — el corte tiene que pasar ANTES de mirar PermisosActuales,
    /// nunca después. Compartida por AuthorizationService.Verificar y PermisoAuthorizationHandler
    /// (Task 7) — una sola fuente de verdad.
    /// </summary>
    public static readonly IReadOnlySet<string> PermisosEstructuralesAdmin = new HashSet<string>
    {
        Permisos.GestionarUsuarios,
        Permisos.ImportarPlanillas,
        Permisos.GestionarDiagnostico,
        Permisos.AdministrarTareas,
    };

    /// <summary>
    /// Los 11 permisos configurables: Permisos.Todos menos los 4 estructurales. Derivado, no
    /// una lista aparte a mano — evita que ambas listas diverjan si algún día se agrega un
    /// permiso nuevo a Permisos.Todos sin decidir explícitamente su categoría acá.
    /// </summary>
    public static readonly IReadOnlyList<string> PermisosConfigurables =
        Permisos.Todos.Where(p => !PermisosEstructuralesAdmin.Contains(p)).ToList();

    /// <summary>
    /// Plantilla de arranque para Operadores nuevos (spec decisión 3): reemplaza a la vieja
    /// AccionesOperador privada. Orden fijo y explícito — no depende del orden de iteración de
    /// un HashSet (mismo criterio que la corrección al backfill de LotesImportacion, af4321b).
    /// Consumida por el backfill de la migración (Task 1, vía PermisoUsuarioBackfillSql, que
    /// tiene esta MISMA lista transcripta a SQL) y por UsuarioService.AltaUsuarioAsync (Task 11).
    /// </summary>
    public static readonly IReadOnlyList<string> PermisosInicialesOperador =
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

    // OBSOLETO: se elimina en la Task 7. Derivada de PermisosInicialesOperador (pre-flight,
    // corrección D) en vez de repetir la misma lista de 9 permisos por segunda vez en el mismo
    // archivo — una sola fuente de verdad, mismo contenido exacto, Verificar(RolUsuario?, string)
    // sigue funcionando idéntico hasta que la Task 7 elimina las dos.
    private static readonly HashSet<string> AccionesOperador = new(PermisosInicialesOperador);

    public void Verificar(RolUsuario? rolActual, string accion)
    {
        if (rolActual is null)
            throw new UnauthorizedAccessException("No hay sesión activa.");

        if (rolActual == RolUsuario.Admin)
            return;

        if (!AccionesOperador.Contains(accion))
            throw new UnauthorizedAccessException(
                $"El rol Operador no tiene permiso para ejecutar la acción '{accion}'.");
    }

    public void Verificar(ICurrentSession sesion, string accion)
    {
        if (!sesion.EstaAutenticado)
            throw new UnauthorizedAccessException("No hay sesión activa.");

        if (sesion.RolActual == RolUsuario.Admin)
            return;

        // Corte ANTES de mirar PermisosActuales (Global Constraints): un Operador nunca pasa
        // acá, sin importar qué haya en la sesión.
        if (PermisosEstructuralesAdmin.Contains(accion))
            throw new UnauthorizedAccessException(
                $"El rol Operador no tiene permiso para ejecutar la acción '{accion}'.");

        if (!sesion.PermisosActuales.Contains(accion))
            throw new UnauthorizedAccessException(
                $"El rol Operador no tiene permiso para ejecutar la acción '{accion}'.");
    }

    public bool TienePermiso(RolUsuario rol, string accion) =>
        rol == RolUsuario.Admin || AccionesOperador.Contains(accion);
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~AuthorizationServiceTests`
Expected: todos PASS (los de antes de esta task + los nuevos).

- [ ] **Step 6: Correr la suite completa de Application**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: todo verde.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Authorization/IAuthorizationService.cs \
        src/StockApp.Application/Authorization/AuthorizationService.cs \
        tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs
git commit -m "feat(permisos): overload Verificar(ICurrentSession, string) con corte estructural"
```

---

### Tasks 6a-6d: Migración de los 96 call sites de `_auth.Verificar(...)`

**Contexto de las 4 partes:** Cada call site sigue textualmente el patrón `_auth.Verificar(_session.RolActual, ` (95 veces, campo `_session`, confirmado por grep — no hay variantes de nombre de campo) o, en el único caso fuera de ese patrón (`BackupsEndpoints.cs`), `auth.Verificar(session.RolActual, ` (variables locales de una lambda, no campos). El cambio es mecánico: quitar `.RolActual`. Se divide en 4 partes por carpeta para que cada commit sea revisable, cada una deja la suite verde porque el overload viejo sigue existiendo hasta la Task 7.

**Gotcha real, no mencionado en el spec:** varios tests de estos servicios configuran `Mock<IAuthorizationService>.Setup(a => a.Verificar(RolUsuario.Admin, ...))` o `Verificar(It.IsAny<RolUsuario?>(), ...)` — esos `Setup` matchean el overload VIEJO por tipo de parámetro. Tras migrar la producción al overload nuevo, esos `Setup` dejan de matchear y Moq (mock *loose*, no *strict*) no lanza nada — la acción sigue de largo en vez de lanzar `UnauthorizedAccessException`, y el test que esperaba esa excepción **se pone rojo** (no se cae en silencio: `Assert.ThrowsAsync` falla al no recibir la excepción). Cada parte de abajo incluye el ajuste de los tests que sí configuran ese mock; los que usan un mock sin configurar (`Mock<IAuthSvc>()` sin `.Setup` sobre `Verificar`) no necesitan tocarse — nunca dependieron del overload.

---

### Task 6a: Catalogo/ + Auth/ (5 archivos, 26 call sites)

**Files:**
- Modify: `src/StockApp.Application/Catalogo/CategoriaService.cs` (5)
- Modify: `src/StockApp.Application/Catalogo/ProductoService.cs` (6)
- Modify: `src/StockApp.Application/Catalogo/ProveedorService.cs` (4)
- Modify: `src/StockApp.Application/Catalogo/UnidadMedidaService.cs` (6)
- Modify: `src/StockApp.Application/Auth/UsuarioService.cs` (5)
- Modify: `tests/StockApp.Application.Tests/Catalogo/CategoriaServiceTests.cs`
- Modify: `tests/StockApp.Application.Tests/Catalogo/CategoriaServiceInvalidacionTests.cs`
- Modify: `tests/StockApp.Application.Tests/Catalogo/ProductoServiceTests.cs`
- Modify: `tests/StockApp.Application.Tests/Catalogo/ProductoServiceInvalidacionTests.cs`
- Modify: `tests/StockApp.Application.Tests/Catalogo/ProveedorServiceTests.cs`
- Modify: `tests/StockApp.Application.Tests/Catalogo/UnidadMedidaServiceTests.cs`
- Modify: `tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs`

**Interfaces:**
- Consumes: `IAuthorizationService.Verificar(ICurrentSession sesion, string accion)` (Task 5).

- [ ] **Step 1: Migrar los call sites de producción**

Run:
```bash
sd '_auth\.Verificar\(_session\.RolActual, ' '_auth.Verificar(_session, ' \
  src/StockApp.Application/Catalogo/CategoriaService.cs \
  src/StockApp.Application/Catalogo/ProductoService.cs \
  src/StockApp.Application/Catalogo/ProveedorService.cs \
  src/StockApp.Application/Catalogo/UnidadMedidaService.cs \
  src/StockApp.Application/Auth/UsuarioService.cs
```

- [ ] **Step 2: Verificar que no quedó ningún call site viejo en estos 5 archivos**

Run: `grep -rn '_auth\.Verificar(_session\.RolActual' src/StockApp.Application/Catalogo/ src/StockApp.Application/Auth/UsuarioService.cs`
Expected: sin resultados (0 líneas).

- [ ] **Step 3: Migrar los mocks de los tests**

Estos 6 archivos configuran `Mock<IAuthorizationService>` con valores literales `RolUsuario.Admin`/`RolUsuario.Operador` como primer argumento — todos con la variable `session` (Mock<ICurrentSession>) declarada en su helper `Crear(...)`. Ejemplo real de `CategoriaServiceTests.cs` antes/después:

```csharp
// Antes:
auth.Setup(a => a.Verificar(RolUsuario.Admin, It.IsAny<string>()));
auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarTablasMaestras))
    .Throws<UnauthorizedAccessException>();

// Después:
auth.Setup(a => a.Verificar(session.Object, It.IsAny<string>()));
auth.Setup(a => a.Verificar(session.Object, Permisos.GestionarTablasMaestras))
    .Throws<UnauthorizedAccessException>();
```

Run (mismo reemplazo mecánico en los 6 archivos — todos usan la variable `session`):
```bash
sd 'Verificar\(RolUsuario\.Admin, ' 'Verificar(session.Object, ' \
  tests/StockApp.Application.Tests/Catalogo/CategoriaServiceTests.cs \
  tests/StockApp.Application.Tests/Catalogo/CategoriaServiceInvalidacionTests.cs \
  tests/StockApp.Application.Tests/Catalogo/ProductoServiceTests.cs \
  tests/StockApp.Application.Tests/Catalogo/ProductoServiceInvalidacionTests.cs \
  tests/StockApp.Application.Tests/Catalogo/ProveedorServiceTests.cs \
  tests/StockApp.Application.Tests/Catalogo/UnidadMedidaServiceTests.cs \
  tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs

sd 'Verificar\(RolUsuario\.Operador, ' 'Verificar(session.Object, ' \
  tests/StockApp.Application.Tests/Catalogo/CategoriaServiceTests.cs \
  tests/StockApp.Application.Tests/Catalogo/CategoriaServiceInvalidacionTests.cs \
  tests/StockApp.Application.Tests/Catalogo/ProductoServiceTests.cs \
  tests/StockApp.Application.Tests/Catalogo/ProductoServiceInvalidacionTests.cs \
  tests/StockApp.Application.Tests/Catalogo/ProveedorServiceTests.cs \
  tests/StockApp.Application.Tests/Catalogo/UnidadMedidaServiceTests.cs \
  tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs
```

`ProductoServiceTests.cs` tiene además una línea `auth.Verify(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarProductos), Times.Once);` — el segundo `sd` de arriba ya la cubre porque matchea el mismo texto `Verificar(RolUsuario.Operador, `.

- [ ] **Step 4: Revisar el diff antes de compilar**

Run: `git diff --stat tests/StockApp.Application.Tests/Catalogo tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs`

Confirmar visualmente (`git diff`) que ningún reemplazo cayó dentro de un comentario o de un `[InlineData]` que no correspondiera — dado que el patrón `Verificar(RolUsuario.Admin, ` / `Verificar(RolUsuario.Operador, ` es específico de `.Setup`/`.Verify` de Moq, no debería haber falsos positivos, pero es la misma cautela que ya aplicó el plan de alertas de backup a su propio `sd`.

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~Catalogo|FullyQualifiedName~UsuarioServiceTests"`
Expected: todos PASS. Si algo da rojo por un `Setup` que no matcheó (mensaje de Moq tipo "Expected invocation... was not performed"), revisar si ese archivo tiene una variable de mock con otro nombre y ajustar a mano.

- [ ] **Step 6: Correr la suite completa de Application**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: todo verde (nada más se tocó fuera de estos archivos).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Catalogo/CategoriaService.cs \
        src/StockApp.Application/Catalogo/ProductoService.cs \
        src/StockApp.Application/Catalogo/ProveedorService.cs \
        src/StockApp.Application/Catalogo/UnidadMedidaService.cs \
        src/StockApp.Application/Auth/UsuarioService.cs \
        tests/StockApp.Application.Tests/Catalogo/ \
        tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs
git commit -m "refactor(permisos): migra call sites de Catalogo y Auth al nuevo Verificar"
```

---

### Task 6b: Finanzas/ parte 1 (6 archivos, 28 call sites)

**Files:**
- Modify: `src/StockApp.Application/Finanzas/AdjuntoService.cs` (6)
- Modify: `src/StockApp.Application/Finanzas/AnalisisImportacionService.cs` (1)
- Modify: `src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs` (3)
- Modify: `src/StockApp.Application/Finanzas/FinanzasVistasService.cs` (4)
- Modify: `src/StockApp.Application/Finanzas/FuenteFinanciamientoService.cs` (5)
- Modify: `src/StockApp.Application/Finanzas/GastoService.cs` (9)
- Modify: `tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs`
- Modify: `tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceGastosTests.cs`
- Modify: `tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceHistorialTests.cs`
- Modify: `tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceTests.cs`

**Interfaces:**
- Consumes: `IAuthorizationService.Verificar(ICurrentSession sesion, string accion)` (Task 5).

- [ ] **Step 1: Migrar los call sites de producción**

Run:
```bash
sd '_auth\.Verificar\(_session\.RolActual, ' '_auth.Verificar(_session, ' \
  src/StockApp.Application/Finanzas/AdjuntoService.cs \
  src/StockApp.Application/Finanzas/AnalisisImportacionService.cs \
  src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs \
  src/StockApp.Application/Finanzas/FinanzasVistasService.cs \
  src/StockApp.Application/Finanzas/FuenteFinanciamientoService.cs \
  src/StockApp.Application/Finanzas/GastoService.cs
```

`AdjuntoService.cs` tiene una línea con ternario como segundo argumento (`_auth.Verificar(_session.RolActual, adjunto.EsDePago ? Permisos.RegistrarPagos : Permisos.RegistrarGastos);`) — el `sd` de arriba la cubre igual porque solo reemplaza el prefijo `_session.RolActual, `.

- [ ] **Step 2: Verificar que no quedó ningún call site viejo**

Run: `grep -rln '_auth\.Verificar(_session\.RolActual' src/StockApp.Application/Finanzas/AdjuntoService.cs src/StockApp.Application/Finanzas/AnalisisImportacionService.cs src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs src/StockApp.Application/Finanzas/FinanzasVistasService.cs src/StockApp.Application/Finanzas/FuenteFinanciamientoService.cs src/StockApp.Application/Finanzas/GastoService.cs`
Expected: sin resultados.

- [ ] **Step 3: Migrar los mocks de los tests**

`FinanzasVistasService*Tests.cs` (4 archivos) y `GastoServiceTests.cs`/`FuenteFinanciamientoServiceTests.cs` **NO** configuran `.Setup` sobre `Verificar` (mock sin configurar) — no necesitan tocarse. Solo estos 4 archivos configuran el mock:

`AdjuntoServiceTests.cs` usa un campo `_session` (con guion bajo, no una variable local `session`) — reemplazo distinto al resto:

```bash
sd 'Verificar\(RolUsuario\.Admin, ' 'Verificar(_session.Object, ' tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs
sd 'Verificar\(RolUsuario\.Operador, ' 'Verificar(_session.Object, ' tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs
```

`AnalisisImportacionServiceGastosTests.cs`, `ConfirmacionImportacionServiceHistorialTests.cs` y `ConfirmacionImportacionServiceTests.cs` usan la variable local `session`:

```bash
sd 'Verificar\(RolUsuario\.Admin, ' 'Verificar(session.Object, ' \
  tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceGastosTests.cs \
  tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceHistorialTests.cs \
  tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceTests.cs

sd 'Verificar\(RolUsuario\.Operador, ' 'Verificar(session.Object, ' \
  tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceGastosTests.cs \
  tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceHistorialTests.cs \
  tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceTests.cs
```

- [ ] **Step 4: Revisar el diff**

Run: `git diff tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceGastosTests.cs tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceHistorialTests.cs tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceTests.cs`

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~Finanzas.AdjuntoService|FullyQualifiedName~Finanzas.AnalisisImportacion|FullyQualifiedName~Finanzas.ConfirmacionImportacion|FullyQualifiedName~Finanzas.FinanzasVistas|FullyQualifiedName~Finanzas.FuenteFinanciamiento|FullyQualifiedName~Finanzas.GastoService"`
Expected: todos PASS.

- [ ] **Step 6: Correr la suite completa de Application**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: todo verde.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Finanzas/AdjuntoService.cs \
        src/StockApp.Application/Finanzas/AnalisisImportacionService.cs \
        src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs \
        src/StockApp.Application/Finanzas/FinanzasVistasService.cs \
        src/StockApp.Application/Finanzas/FuenteFinanciamientoService.cs \
        src/StockApp.Application/Finanzas/GastoService.cs \
        tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs \
        tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceGastosTests.cs \
        tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceHistorialTests.cs \
        tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceTests.cs
git commit -m "refactor(permisos): migra call sites de Finanzas (parte 1) al nuevo Verificar"
```

---

### Task 6c: Finanzas/ parte 2 + Movimientos/ (5 archivos, 22 call sites)

**Files:**
- Modify: `src/StockApp.Application/Finanzas/IngresoCajaService.cs` (4)
- Modify: `src/StockApp.Application/Finanzas/LineaPoaService.cs` (5)
- Modify: `src/StockApp.Application/Finanzas/RubroGastoService.cs` (5)
- Modify: `src/StockApp.Application/Movimientos/IngresoPorFacturaService.cs` (5)
- Modify: `src/StockApp.Application/Movimientos/MovimientoStockService.cs` (3)
- Modify: `tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs`
- Modify: `tests/StockApp.Application.Tests/Movimientos/MovimientoStockServiceTests.cs`

**Interfaces:**
- Consumes: `IAuthorizationService.Verificar(ICurrentSession sesion, string accion)` (Task 5).

- [ ] **Step 1: Migrar los call sites de producción**

Run:
```bash
sd '_auth\.Verificar\(_session\.RolActual, ' '_auth.Verificar(_session, ' \
  src/StockApp.Application/Finanzas/IngresoCajaService.cs \
  src/StockApp.Application/Finanzas/LineaPoaService.cs \
  src/StockApp.Application/Finanzas/RubroGastoService.cs \
  src/StockApp.Application/Movimientos/IngresoPorFacturaService.cs \
  src/StockApp.Application/Movimientos/MovimientoStockService.cs
```

- [ ] **Step 2: Verificar que no quedó ningún call site viejo**

Run: `grep -rln '_auth\.Verificar(_session\.RolActual' src/StockApp.Application/Finanzas/IngresoCajaService.cs src/StockApp.Application/Finanzas/LineaPoaService.cs src/StockApp.Application/Finanzas/RubroGastoService.cs src/StockApp.Application/Movimientos/`
Expected: sin resultados.

- [ ] **Step 3: Migrar los mocks de los tests**

`IngresoCajaServiceTests.cs`, `LineaPoaServiceTests.cs` y `RubroGastoServiceTests.cs` **NO** configuran `.Setup` sobre `Verificar` — no se tocan. `IngresoPorFacturaServiceTests.cs` y `MovimientoStockServiceTests.cs` sí, pero con el matcher `It.IsAny<RolUsuario?>()` en vez de un valor literal (matchea el overload viejo por TIPO, no por valor — deja de matchear igual tras la migración). Ejemplo real de `MovimientoStockServiceTests.cs` antes/después:

```csharp
// Antes:
auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));
auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.RegistrarMovimientos))
    .Throws<UnauthorizedAccessException>();

// Después:
auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), It.IsAny<string>()));
auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.RegistrarMovimientos))
    .Throws<UnauthorizedAccessException>();
```

Run:
```bash
sd 'Verificar\(It\.IsAny<RolUsuario\?>\(\), ' 'Verificar(It.IsAny<ICurrentSession>(), ' \
  tests/StockApp.Application.Tests/Movimientos/IngresoPorFacturaServiceTests.cs \
  tests/StockApp.Application.Tests/Movimientos/MovimientoStockServiceTests.cs
```

Confirmar que ambos archivos ya tienen `using StockApp.Application.Interfaces;` al tope (necesario para `ICurrentSession`) — si falta, agregarlo.

- [ ] **Step 4: Revisar el diff**

Run: `git diff tests/StockApp.Application.Tests/Movimientos/`

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~Finanzas.IngresoCaja|FullyQualifiedName~Finanzas.LineaPoa|FullyQualifiedName~Finanzas.RubroGasto|FullyQualifiedName~Movimientos"`
Expected: todos PASS.

- [ ] **Step 6: Correr la suite completa de Application**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: todo verde.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Finanzas/IngresoCajaService.cs \
        src/StockApp.Application/Finanzas/LineaPoaService.cs \
        src/StockApp.Application/Finanzas/RubroGastoService.cs \
        src/StockApp.Application/Movimientos/ \
        tests/StockApp.Application.Tests/Movimientos/
git commit -m "refactor(permisos): migra call sites de Finanzas (parte 2) y Movimientos al nuevo Verificar"
```

---

### Task 6d: Reportes/ + Auditoria/ + Backups/ + Alertas/ + Tareas/ + Api (6 archivos, 20 call sites)

**Files:**
- Modify: `src/StockApp.Application/Reportes/ReporteStockService.cs` (4)
- Modify: `src/StockApp.Application/Auditoria/AuditoriaQueryService.cs` (1)
- Modify: `src/StockApp.Application/Backups/ServicioConsultaBackups.cs` (3)
- Modify: `src/StockApp.Application/Alertas/ServicioConfiguracionAlertas.cs` (3)
- Modify: `src/StockApp.Application/Tareas/TareaService.cs` (8)
- Modify: `src/StockApp.Api/Endpoints/BackupsEndpoints.cs` (1 — patrón distinto, variables locales)
- Modify: `tests/StockApp.Application.Tests/Auditoria/AuditoriaQueryServiceTests.cs`
- Modify: `tests/StockApp.Application.Tests/Backups/ServicioConsultaBackupsTests.cs`
- Modify: `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs`

**Interfaces:**
- Consumes: `IAuthorizationService.Verificar(ICurrentSession sesion, string accion)` (Task 5).

- [ ] **Step 1: Migrar los call sites de producción en Application**

Run:
```bash
sd '_auth\.Verificar\(_session\.RolActual, ' '_auth.Verificar(_session, ' \
  src/StockApp.Application/Reportes/ReporteStockService.cs \
  src/StockApp.Application/Auditoria/AuditoriaQueryService.cs \
  src/StockApp.Application/Backups/ServicioConsultaBackups.cs \
  src/StockApp.Application/Alertas/ServicioConfiguracionAlertas.cs \
  src/StockApp.Application/Tareas/TareaService.cs
```

- [ ] **Step 2: Migrar el call site restante en `BackupsEndpoints.cs`**

En `src/StockApp.Api/Endpoints/BackupsEndpoints.cs`, dentro de `MapPost("/backups", ...)`, reemplazar:

```csharp
            auth.Verificar(session.RolActual, Permisos.GestionarDiagnostico);
```

por:

```csharp
            auth.Verificar(session, Permisos.GestionarDiagnostico);
```

(Las variables `session`/`auth` son los parámetros del lambda `(ICurrentSession session, IAuthorizationService auth, DisparadorBackupManual disparador) => {...}` — nada más cambia en ese endpoint.)

- [ ] **Step 3: Verificar que no quedó ningún call site viejo en todo el repo**

Run: `grep -rn '\.Verificar(_\?session\.RolActual' src/StockApp.Application/ src/StockApp.Api/`
Expected: sin resultados — este es el grep que cierra la migración completa de los 96 call sites.

- [ ] **Step 4: Migrar los mocks de los tests**

`ReporteStockService*Tests.cs` (4 archivos bajo `Reportes/`) y `ServicioConfiguracionAlertasTests.cs` no necesitan tocarse: los primeros no configuran `.Setup` sobre `Verificar`, y `ServicioConfiguracionAlertasTests.cs` usa una instancia REAL de `AuthorizationService` (no un mock) con un `SesionFake : ICurrentSession` propio — ya compatible con el overload nuevo desde la Task 4 (donde se le agregaron los miembros nuevos de la interfaz). `AuditoriaQueryServiceTests.cs` usa el matcher `It.IsAny<RolUsuario?>()`; `ServicioConsultaBackupsTests.cs` y `TareaServiceTests.cs` usan valores literales con variable `session`.

```bash
sd 'Verificar\(It\.IsAny<RolUsuario\?>\(\), ' 'Verificar(It.IsAny<ICurrentSession>(), ' \
  tests/StockApp.Application.Tests/Auditoria/AuditoriaQueryServiceTests.cs

sd 'Verificar\(RolUsuario\.Admin, ' 'Verificar(session.Object, ' \
  tests/StockApp.Application.Tests/Backups/ServicioConsultaBackupsTests.cs \
  tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs

sd 'Verificar\(RolUsuario\.Operador, ' 'Verificar(session.Object, ' \
  tests/StockApp.Application.Tests/Backups/ServicioConsultaBackupsTests.cs \
  tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs
```

Confirmar que `AuditoriaQueryServiceTests.cs` tiene `using StockApp.Application.Interfaces;` (para `ICurrentSession`); agregarlo si falta.

- [ ] **Step 5: Revisar el diff**

Run: `git diff tests/StockApp.Application.Tests/Auditoria/ tests/StockApp.Application.Tests/Backups/ tests/StockApp.Application.Tests/Tareas/`

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~Reportes|FullyQualifiedName~Auditoria|FullyQualifiedName~Backups|FullyQualifiedName~Alertas|FullyQualifiedName~Tareas"`
Expected: todos PASS.

- [ ] **Step 7: Correr la suite completa de Application y de Api**

Run: `dotnet test tests/StockApp.Application.Tests && dotnet test tests/StockApp.Api.Tests`
Expected: todo verde. Esta es la última parte de la migración — con esto, los 96 call sites (95 en Application + 1 en `BackupsEndpoints.cs`) usan el overload nuevo, y `Verificar(RolUsuario?, string)`/`TienePermiso` quedan sin consumidores propios fuera de `Program.cs` (que se migra en la Task 7).

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Application/Reportes/ReporteStockService.cs \
        src/StockApp.Application/Auditoria/AuditoriaQueryService.cs \
        src/StockApp.Application/Backups/ServicioConsultaBackups.cs \
        src/StockApp.Application/Alertas/ServicioConfiguracionAlertas.cs \
        src/StockApp.Application/Tareas/TareaService.cs \
        src/StockApp.Api/Endpoints/BackupsEndpoints.cs \
        tests/StockApp.Application.Tests/Auditoria/AuditoriaQueryServiceTests.cs \
        tests/StockApp.Application.Tests/Backups/ServicioConsultaBackupsTests.cs \
        tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs
git commit -m "refactor(permisos): migra los call sites restantes (Reportes, Auditoria, Backups, Alertas, Tareas, Api) al nuevo Verificar"
```

---

### Task 7: `PermisoRequirement` + `PermisoAuthorizationHandler` + reemplazo del bloque de policies + limpieza de la interfaz

**Files:**
- Create: `src/StockApp.Api/Auth/PermisoRequirement.cs`
- Create: `src/StockApp.Api/Auth/PermisoAuthorizationHandler.cs`
- Modify: `src/StockApp.Api/Program.cs` (bloque `AddAuthorization`, líneas ~412-430)
- Modify: `src/StockApp.Application/Authorization/IAuthorizationService.cs` (elimina miembros viejos)
- Modify: `src/StockApp.Application/Authorization/AuthorizationService.cs` (elimina miembros viejos)
- Modify: `tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs` (elimina tests del overload viejo)
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs` (gap real, Step 11a: usaba `TienePermiso` directo)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs` (Step 11a)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs` (Step 11a — call site de `AdjuntosPanelViewModel`)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/CalendarioPagosViewModelTests.cs` (Step 11a — call site de `AdjuntosPanelViewModel`)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/PagosGastoViewModelTests.cs` (Step 11a — call site de `AdjuntosPanelViewModel`)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs` (Step 11a — 2 call sites de `AdjuntosPanelViewModel`)
- Test: `tests/StockApp.Api.Tests/Auth/PermisoAuthorizationHandlerTests.cs`
- Test: `tests/StockApp.Api.Tests/Auth/PermisosEndpointGuardTests.cs`

**Interfaces:**
- Consumes: `AuthorizationService.PermisosEstructuralesAdmin` (Task 5, static); `IProveedorPermisos.ObtenerAsync(int)` (Task 3); `StockAppClaimTypes.Rol`/`.UsuarioId` (existente).
- Produces: `PermisoRequirement(string Permiso)` : `IAuthorizationRequirement`; `PermisoAuthorizationHandler`.

- [ ] **Step 1: Escribir el test del handler que falla**

`tests/StockApp.Api.Tests/Auth/PermisoAuthorizationHandlerTests.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using StockApp.Api.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class PermisoAuthorizationHandlerTests
{
    private sealed class ProveedorPermisosFake : IProveedorPermisos
    {
        public HashSet<string> Permisos { get; set; } = new();
        public Task<IReadOnlySet<string>> ObtenerAsync(int usuarioId) => Task.FromResult<IReadOnlySet<string>>(Permisos);
        public Task GuardarAsync(int usuarioId, IReadOnlyCollection<string> permisos) => Task.CompletedTask;
    }

    private static ClaimsPrincipal Usuario(int id, string rol) => new(new ClaimsIdentity(
        new[]
        {
            new Claim(StockAppClaimTypes.UsuarioId, id.ToString()),
            new Claim(StockAppClaimTypes.Rol, rol),
        },
        authenticationType: "Test"));

    private static async Task<bool> EvaluarAsync(
        ClaimsPrincipal usuario, string permisoRequerido, ProveedorPermisosFake proveedor)
    {
        var handler = new PermisoAuthorizationHandler(proveedor);
        var requirement = new PermisoRequirement(permisoRequerido);
        var context = new AuthorizationHandlerContext(new[] { requirement }, usuario, resource: null);

        await handler.HandleAsync(context);

        return context.HasSucceeded;
    }

    [Fact]
    public async Task Admin_PasaCualquierPermiso_SinConsultarElProveedor()
    {
        var proveedor = new ProveedorPermisosFake();

        var exito = await EvaluarAsync(Usuario(1, "Admin"), Permisos.GestionarUsuarios, proveedor);

        Assert.True(exito);
    }

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.ImportarPlanillas)]
    [InlineData(Permisos.GestionarDiagnostico)]
    [InlineData(Permisos.AdministrarTareas)]
    public async Task Operador_LosCuatroEstructurales_RechazanAunqueElProveedorLosTuviera(string permisoEstructural)
    {
        // Sesión "envenenada": el proveedor devuelve el permiso estructural, pero el handler
        // tiene que rechazar igual — el corte es ANTES de consultar el proveedor.
        var proveedor = new ProveedorPermisosFake { Permisos = { permisoEstructural } };

        var exito = await EvaluarAsync(Usuario(2, "Operador"), permisoEstructural, proveedor);

        Assert.False(exito);
    }

    [Fact]
    public async Task Operador_ConElPermisoEnElProveedor_Pasa()
    {
        var proveedor = new ProveedorPermisosFake { Permisos = { Permisos.VerFinanzas } };

        var exito = await EvaluarAsync(Usuario(2, "Operador"), Permisos.VerFinanzas, proveedor);

        Assert.True(exito);
    }

    [Fact]
    public async Task Operador_SinElPermisoEnElProveedor_Rechaza()
    {
        var proveedor = new ProveedorPermisosFake { Permisos = { Permisos.GestionarProductos } };

        var exito = await EvaluarAsync(Usuario(2, "Operador"), Permisos.VerFinanzas, proveedor);

        Assert.False(exito);
    }

    [Fact]
    public async Task SinClaimDeRol_Rechaza()
    {
        var sinRol = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(StockAppClaimTypes.UsuarioId, "3") }, authenticationType: "Test"));

        var exito = await EvaluarAsync(sinRol, Permisos.VerFinanzas, new ProveedorPermisosFake());

        Assert.False(exito);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~PermisoAuthorizationHandlerTests`
Expected: FALLA de compilación — `PermisoRequirement` y `PermisoAuthorizationHandler` no existen.

- [ ] **Step 3: Crear el requirement**

`src/StockApp.Api/Auth/PermisoRequirement.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace StockApp.Api.Auth;

/// <summary>Requirement de un permiso concreto (spec 2026-08-10). Reemplaza el RequireClaim
/// fijo por rol: la policy sigue llamándose igual que el permiso (Permisos.X), así que los 32
/// endpoints existentes no cambian una sola línea de `.RequireAuthorization(Permisos.X)`.</summary>
public record PermisoRequirement(string Permiso) : IAuthorizationRequirement;
```

- [ ] **Step 4: Implementar el handler**

`src/StockApp.Api/Auth/PermisoAuthorizationHandler.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;

namespace StockApp.Api.Auth;

/// <summary>
/// Barrera HTTP de permisos (spec 2026-08-10). Reemplaza el RequireClaim fijo por rol que
/// Program.cs derivaba de AuthorizationService.TienePermiso al arrancar — ahora resuelve
/// contra los permisos reales del usuario en cada request. Scoped: inyecta IProveedorPermisos
/// (Singleton) y lee los claims del usuario actual desde el AuthorizationHandlerContext.
///
/// Orden crítico (Global Constraints): el corte por PermisosEstructuralesAdmin va SIEMPRE
/// antes de consultar el proveedor — un Operador nunca llega a esa consulta para uno de los
/// 4 permisos estructurales, sin importar qué hubiera en la tabla.
/// </summary>
public class PermisoAuthorizationHandler : AuthorizationHandler<PermisoRequirement>
{
    private readonly IProveedorPermisos _proveedor;

    public PermisoAuthorizationHandler(IProveedorPermisos proveedor) => _proveedor = proveedor;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermisoRequirement requirement)
    {
        var rolClaim = context.User.FindFirst(StockAppClaimTypes.Rol)?.Value;
        if (rolClaim is null || !Enum.TryParse<RolUsuario>(rolClaim, out var rol))
            return;

        if (rol == RolUsuario.Admin)
        {
            context.Succeed(requirement);
            return;
        }

        if (AuthorizationService.PermisosEstructuralesAdmin.Contains(requirement.Permiso))
            return; // Operador nunca — corte antes de tocar el proveedor.

        var usuarioIdClaim = context.User.FindFirst(StockAppClaimTypes.UsuarioId)?.Value;
        if (usuarioIdClaim is null || !int.TryParse(usuarioIdClaim, out var usuarioId))
            return;

        var permisos = await _proveedor.ObtenerAsync(usuarioId);
        if (permisos.Contains(requirement.Permiso))
            context.Succeed(requirement);
    }
}
```

- [ ] **Step 5: Correr el test del handler y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~PermisoAuthorizationHandlerTests`
Expected: 8 tests PASS (4 del `[Theory]` + 4 `[Fact]`).

- [ ] **Step 6: Escribir el test guardián que falla**

Este test enumera **todos** los endpoints autorizados por un único `Permisos.X` (excluye los 2 casos con múltiples policies AND — `POST/GET .../adjuntos/{id}/contenido` y `DELETE /finanzas/adjuntos/{id}` de `AdjuntosEndpoints.cs`, que no exigen un único permiso) y verifica, vía reflexión sobre el `EndpointDataSource` real del host, que cada uno sigue exigiendo exactamente el mismo permiso que exigía ANTES de este cambio — protege contra que alguien borre o cambie un `.RequireAuthorization` durante el reemplazo del bloque de `Program.cs`.

`tests/StockApp.Api.Tests/Auth/PermisosEndpointGuardTests.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Authorization;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class PermisosEndpointGuardTests : ApiTestBase
{
    public PermisosEndpointGuardTests(ApiFactory factory) : base(factory) { }

    /// <summary>
    /// Fixture congelada (Metodo, Ruta, Permiso) — construida a partir del código real de
    /// src/StockApp.Api/Endpoints/*.cs al momento de escribir esta task. Cualquier fila que
    /// deje de matchear tras un cambio en un archivo de Endpoints es una regresión real.
    /// </summary>
    private static readonly (string Metodo, string Ruta, string Permiso)[] EndpointsYPermisos =
    [
        ("POST",   "/finanzas/gastos/{id}/adjuntos", Permisos.RegistrarGastos),
        ("POST",   "/finanzas/pagos/{id}/adjuntos", Permisos.RegistrarPagos),
        ("GET",    "/finanzas/gastos/{id}/adjuntos", Permisos.VerFinanzas),
        ("GET",    "/finanzas/pagos/{id}/adjuntos", Permisos.VerFinanzas),
        ("GET",    "/finanzas/adjuntos/{id}/contenido", Permisos.VerFinanzas),

        ("GET",    "/auditoria", Permisos.VerReportes),

        ("GET",    "/backups", Permisos.GestionarDiagnostico),
        ("GET",    "/backups/{id}/contenido", Permisos.GestionarDiagnostico),
        ("GET",    "/backups/salud", Permisos.GestionarDiagnostico),
        ("POST",   "/backups", Permisos.GestionarDiagnostico),

        ("GET",    "/categorias", Permisos.GestionarTablasMaestras),
        ("POST",   "/categorias", Permisos.GestionarTablasMaestras),
        ("PUT",    "/categorias/{id}", Permisos.GestionarTablasMaestras),
        ("DELETE", "/categorias/{id}", Permisos.GestionarTablasMaestras),
        ("GET",    "/categorias/activas", Permisos.GestionarProductos),

        ("GET",    "/configuracion/alertas", Permisos.GestionarDiagnostico),
        ("PUT",    "/configuracion/alertas", Permisos.GestionarDiagnostico),
        ("POST",   "/configuracion/alertas/probar", Permisos.GestionarDiagnostico),

        ("GET",    "/finanzas/libro-caja", Permisos.VerFinanzas),
        ("GET",    "/finanzas/control-poa", Permisos.VerFinanzas),
        ("GET",    "/finanzas/calendario-pagos", Permisos.VerFinanzas),

        ("GET",    "/finanzas/fuentes", Permisos.GestionarMaestrosFinanzas),
        ("POST",   "/finanzas/fuentes", Permisos.GestionarMaestrosFinanzas),
        ("PUT",    "/finanzas/fuentes/{id}", Permisos.GestionarMaestrosFinanzas),
        ("DELETE", "/finanzas/fuentes/{id}", Permisos.GestionarMaestrosFinanzas),
        ("GET",    "/finanzas/fuentes/activas", Permisos.VerFinanzas),

        ("GET",    "/finanzas/gastos", Permisos.VerFinanzas),
        ("GET",    "/finanzas/gastos/{id}", Permisos.VerFinanzas),
        ("GET",    "/finanzas/gastos/por-factura", Permisos.VerFinanzas),
        ("POST",   "/finanzas/gastos", Permisos.RegistrarGastos),
        ("PUT",    "/finanzas/gastos/{id}", Permisos.RegistrarGastos),
        ("DELETE", "/finanzas/gastos/{id}", Permisos.RegistrarGastos),
        ("POST",   "/finanzas/gastos/{id}/pagos", Permisos.RegistrarPagos),
        ("DELETE", "/finanzas/gastos/{id}/pagos/{pagoId}", Permisos.RegistrarPagos),
        ("POST",   "/finanzas/gastos/{id}/movimientos", Permisos.RegistrarGastos),

        ("POST",   "/finanzas/importar/analizar", Permisos.ImportarPlanillas),
        ("POST",   "/finanzas/importar/confirmar", Permisos.ImportarPlanillas),
        ("POST",   "/finanzas/importar/revertir/{id}", Permisos.ImportarPlanillas),
        ("GET",    "/finanzas/importar/historial", Permisos.ImportarPlanillas),

        ("POST",   "/movimientos/ingreso-factura", Permisos.RegistrarMovimientos),
        ("POST",   "/movimientos/ingreso-factura/{gastoId}/anular", Permisos.RegistrarMovimientos),

        ("GET",    "/finanzas/ingresos", Permisos.VerFinanzas),
        ("POST",   "/finanzas/ingresos", Permisos.RegistrarIngresos),
        ("PUT",    "/finanzas/ingresos/{id}", Permisos.RegistrarIngresos),
        ("DELETE", "/finanzas/ingresos/{id}", Permisos.RegistrarIngresos),

        ("GET",    "/finanzas/lineas-poa", Permisos.GestionarMaestrosFinanzas),
        ("POST",   "/finanzas/lineas-poa", Permisos.GestionarMaestrosFinanzas),
        ("PUT",    "/finanzas/lineas-poa/{id}", Permisos.GestionarMaestrosFinanzas),
        ("DELETE", "/finanzas/lineas-poa/{id}", Permisos.GestionarMaestrosFinanzas),
        ("GET",    "/finanzas/lineas-poa/activas", Permisos.VerFinanzas),

        ("GET",    "/logs", Permisos.GestionarDiagnostico),
        ("GET",    "/logs/contenido", Permisos.GestionarDiagnostico),

        ("POST",   "/movimientos", Permisos.RegistrarMovimientos),
        ("GET",    "/movimientos/historial", Permisos.RegistrarMovimientos),

        ("GET",    "/productos", Permisos.GestionarProductos),
        ("POST",   "/productos", Permisos.GestionarProductos),
        ("PUT",    "/productos/{id}", Permisos.GestionarProductos),
        ("DELETE", "/productos/{id}", Permisos.GestionarProductos),
        ("PUT",    "/productos/{id}/precio", Permisos.GestionarProductos),
        ("POST",   "/productos/{id}/recalcular-stock", Permisos.RecalcularStock),

        ("GET",    "/proveedores", Permisos.GestionarTablasMaestras),
        ("POST",   "/proveedores", Permisos.GestionarTablasMaestras),
        ("PUT",    "/proveedores/{id}", Permisos.GestionarTablasMaestras),
        ("DELETE", "/proveedores/{id}", Permisos.GestionarTablasMaestras),

        ("GET",    "/reportes/valorizacion", Permisos.VerReportes),
        ("GET",    "/reportes/stock-por-categoria", Permisos.VerReportes),
        ("GET",    "/reportes/mas-movidos", Permisos.VerReportes),
        ("GET",    "/reportes/historial-producto/{productoId}", Permisos.VerReportes),

        ("GET",    "/finanzas/rubros", Permisos.GestionarMaestrosFinanzas),
        ("POST",   "/finanzas/rubros", Permisos.GestionarMaestrosFinanzas),
        ("PUT",    "/finanzas/rubros/{id}", Permisos.GestionarMaestrosFinanzas),
        ("DELETE", "/finanzas/rubros/{id}", Permisos.GestionarMaestrosFinanzas),
        ("GET",    "/finanzas/rubros/activos", Permisos.VerFinanzas),

        ("POST",   "/tareas", Permisos.GestionarTareas),
        ("GET",    "/tareas", Permisos.GestionarTareas),
        ("POST",   "/tareas/{id}/tomar", Permisos.GestionarTareas),
        ("POST",   "/tareas/{id}/soltar", Permisos.GestionarTareas),
        ("POST",   "/tareas/{id}/terminar", Permisos.GestionarTareas),
        ("POST",   "/tareas/{id}/cancelar", Permisos.AdministrarTareas),
        ("POST",   "/tareas/{id}/prioridad", Permisos.AdministrarTareas),
        ("POST",   "/tareas/{id}/notas", Permisos.GestionarTareas),

        ("GET",    "/unidades-medida", Permisos.GestionarTablasMaestras),
        ("POST",   "/unidades-medida", Permisos.GestionarTablasMaestras),
        ("PUT",    "/unidades-medida/{id}", Permisos.GestionarTablasMaestras),
        ("DELETE", "/unidades-medida/{id}", Permisos.GestionarTablasMaestras),
        ("GET",    "/unidades-medida/activas", Permisos.GestionarProductos),
        ("POST",   "/unidades-medida/garantizar-por-defecto", Permisos.GestionarProductos),

        ("GET",    "/usuarios", Permisos.GestionarUsuarios),
        ("POST",   "/usuarios", Permisos.GestionarUsuarios),
        ("DELETE", "/usuarios/{id}", Permisos.GestionarUsuarios),
        ("PUT",    "/usuarios/{id}/rol", Permisos.GestionarUsuarios),
        ("PUT",    "/usuarios/{id}/contrasena", Permisos.GestionarUsuarios),
    ];

    [Fact]
    public void CadaEndpointDeLaLista_SigueExigiendoElMismoPermisoQueAntes()
    {
        var endpointDataSource = Factory.Services.GetRequiredService<EndpointDataSource>();
        var endpointsReales = endpointDataSource.Endpoints.OfType<RouteEndpoint>().ToList();

        var faltantes = new List<string>();
        var incorrectos = new List<string>();

        foreach (var (metodo, ruta, permisoEsperado) in EndpointsYPermisos)
        {
            // RoutePattern.RawText conserva las restricciones de ruta tal cual se escribieron
            // (ej. "/productos/{id:int}", no "/productos/{id}") — la fixture de arriba usa la
            // forma sin restricción por legibilidad, así que se normalizan ambos lados quitando
            // el sufijo ":tipo" antes de comparar.
            var candidato = endpointsReales.FirstOrDefault(e =>
            {
                var rutaSinRestriccion = System.Text.RegularExpressions.Regex.Replace(
                    e.RoutePattern.RawText ?? string.Empty, @":[a-zA-Z]+(?=\}|\?)", string.Empty);
                return rutaSinRestriccion.TrimEnd('/') == ruta.TrimEnd('/') &&
                    e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(metodo) == true;
            });

            if (candidato is null)
            {
                faltantes.Add($"{metodo} {ruta}");
                continue;
            }

            var policyNames = candidato.Metadata
                .OfType<IAuthorizeData>()
                .Select(a => a.Policy)
                .Where(p => p is not null)
                .ToList();

            if (!policyNames.Contains(permisoEsperado))
                incorrectos.Add($"{metodo} {ruta}: esperaba '{permisoEsperado}', encontró [{string.Join(", ", policyNames)}]");
        }

        Assert.True(faltantes.Count == 0, $"Endpoints no encontrados: {string.Join("; ", faltantes)}");
        Assert.True(incorrectos.Count == 0, $"Permisos cambiados: {string.Join("; ", incorrectos)}");
    }
}
```

- [ ] **Step 7: Correr el test guardián y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~PermisosEndpointGuardTests`
Expected: PASA de casualidad en este punto (el bloque de policies de `Program.cs` todavía arma las policies con `RequireClaim`, pero `IAuthorizeData.Policy` sigue siendo el string del permiso — el nombre de policy no cambia hasta el Step 8). Si pasa, es la línea base correcta antes del reemplazo; si falla, revisar la fixture contra el código real antes de continuar — no ajustar el código para que el test pase.

- [ ] **Step 8: Reemplazar el bloque de policies en `Program.cs`**

Reemplazar en `src/StockApp.Api/Program.cs` (líneas ~412-430):

```csharp
// Políticas derivadas de AuthorizationService (Fase 2b, D1): NO se declaran a mano.
// Para cada permiso de Permisos.Todos, se arma la política con los roles que
// AuthorizationService.TienePermiso autoriza — una sola fuente de verdad para la
// tabla rol→permiso, compartida entre la API (primera barrera) y los servicios de
// aplicación (segunda barrera, defensa en profundidad — D2).
var authServiceParaPoliticas = new AuthorizationService();
builder.Services.AddAuthorization(options =>
{
    foreach (var permiso in Permisos.Todos)
    {
        var rolesPermitidos = Enum.GetValues<RolUsuario>()
            .Where(rol => authServiceParaPoliticas.TienePermiso(rol, permiso))
            .Select(rol => rol.ToString())
            .ToArray();

        options.AddPolicy(permiso, policy =>
            policy.RequireClaim(StockAppClaimTypes.Rol, rolesPermitidos));
    }
});
```

por:

```csharp
// Políticas derivadas de un AuthorizationHandler (spec 2026-08-10): reemplaza el RequireClaim
// fijo por rol. Cada policy sigue llamándose igual que el permiso (Permisos.X) — los 32
// endpoints existentes no cambian ni una línea de .RequireAuthorization(Permisos.X). El
// handler resuelve contra los permisos reales del usuario (PermisoAuthorizationHandler,
// Api/Auth/), consultando IProveedorPermisos solo quando el permiso no es uno de los 4
// estructurales (AuthorizationService.PermisosEstructuralesAdmin).
builder.Services.AddScoped<IAuthorizationHandler, PermisoAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    foreach (var permiso in Permisos.Todos)
    {
        options.AddPolicy(permiso, policy =>
            policy.Requirements.Add(new PermisoRequirement(permiso)));
    }
});
```

Agregar `using StockApp.Api.Auth;` si no está ya (sí está, por `HttpCurrentSession`/`JwtOptions`/etc.).

- [ ] **Step 9: Correr el test guardián y verificar que sigue pasando**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~PermisosEndpointGuardTests`
Expected: PASS — el handler nuevo resuelve exactamente los mismos permisos por endpoint que el `RequireClaim` viejo.

- [ ] **Step 10: Correr la suite completa de Api para descartar regresiones**

Run: `dotnet test tests/StockApp.Api.Tests`
Expected: todo verde — este es el punto crítico donde, si el corte de `PermisosEstructuralesAdmin` estuviera mal ubicado o el handler tuviera un bug, la matriz 401/403/200 de TODOS los endpoints existentes lo mostraría.

- [ ] **Step 11: Eliminar los miembros viejos de `IAuthorizationService`**

Ya no quedan consumidores de `Verificar(RolUsuario?, string)` ni `TienePermiso` (Program.cs se migró en el Step 8, los 96 call sites en las Tasks 6a-6d). Reemplazar `src/StockApp.Application/Authorization/IAuthorizationService.cs`:

```csharp
using StockApp.Application.Interfaces;

namespace StockApp.Application.Authorization;

/// <summary>
/// Guard de autorización. Cada servicio de Application llama a <see cref="Verificar"/> al
/// inicio de los métodos que requieren permiso.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Verifica que la sesión completa (rol + permisos configurables ya resueltos por el
    /// middleware, spec 2026-08-10) puede ejecutar <paramref name="accion"/>. SINCRÓNICO:
    /// no hace ningún SELECT, lee sesion.PermisosActuales, ya poblado antes de que cualquier
    /// servicio de Application se ejecute.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Sin sesión, o sin el permiso requerido.</exception>
    void Verificar(ICurrentSession sesion, string accion);
}
```

Y en `src/StockApp.Application/Authorization/AuthorizationService.cs`, eliminar el método `Verificar(RolUsuario? rolActual, string accion)`, el método `TienePermiso(RolUsuario, string)` y el campo privado `AccionesOperador` (ya sin consumidores — `PermisosInicialesOperador` lo reemplazó en la Task 5). El archivo queda:

```csharp
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;

namespace StockApp.Application.Authorization;

/// <summary>
/// Implementación de <see cref="IAuthorizationService"/>. Admin tiene acceso a todo. Para
/// Operador, cuatro permisos son estructuralmente Admin-only (nunca se resuelven contra
/// PermisoUsuario) y los 11 restantes se resuelven contra ICurrentSession.PermisosActuales,
/// ya poblado por el middleware (Task 8) antes de que cualquier servicio de Application se
/// ejecute (spec 2026-08-10).
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    /// <summary>
    /// Los 4 permisos que NUNCA se resuelven contra PermisoUsuario: Admin los tiene siempre,
    /// Operador nunca, sin consultar la tabla ni la cache. Punto de falla más peligroso del
    /// diseño (spec, "Riesgos"). Compartida por Verificar y PermisoAuthorizationHandler.
    /// </summary>
    public static readonly IReadOnlySet<string> PermisosEstructuralesAdmin = new HashSet<string>
    {
        Permisos.GestionarUsuarios,
        Permisos.ImportarPlanillas,
        Permisos.GestionarDiagnostico,
        Permisos.AdministrarTareas,
    };

    /// <summary>Los 11 permisos configurables: Permisos.Todos menos los 4 estructurales.</summary>
    public static readonly IReadOnlyList<string> PermisosConfigurables =
        Permisos.Todos.Where(p => !PermisosEstructuralesAdmin.Contains(p)).ToList();

    /// <summary>
    /// Plantilla de arranque para Operadores nuevos (spec decisión 3). Orden fijo y explícito.
    /// Consumida por el backfill de la migración (Task 1) y por UsuarioService.AltaUsuarioAsync
    /// (Task 11).
    /// </summary>
    public static readonly IReadOnlyList<string> PermisosInicialesOperador =
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

    public void Verificar(ICurrentSession sesion, string accion)
    {
        if (!sesion.EstaAutenticado)
            throw new UnauthorizedAccessException("No hay sesión activa.");

        if (sesion.RolActual == RolUsuario.Admin)
            return;

        if (PermisosEstructuralesAdmin.Contains(accion))
            throw new UnauthorizedAccessException(
                $"El rol Operador no tiene permiso para ejecutar la acción '{accion}'.");

        if (!sesion.PermisosActuales.Contains(accion))
            throw new UnauthorizedAccessException(
                $"El rol Operador no tiene permiso para ejecutar la acción '{accion}'.");
    }
}
```

- [ ] **Step 11a: Gap real hallado en Presentation — `AdjuntosPanelViewModel` usa `TienePermiso` directamente**

`grep -rn "TienePermiso" src/StockApp.Presentation/` muestra un consumidor que el spec no menciona: `src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs:59` hace `PuedeModificar = _session.RolActual is RolUsuario rol && _authorization.TienePermiso(rol, accion);`. Al eliminar `TienePermiso` de la interfaz en el Step 11, esto deja de compilar — hay que arreglarlo en esta misma task, no más adelante.

Reemplazar en `src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs`:

```csharp
    private readonly IAdjuntoService _adjuntos;
    private readonly IServicioSeleccionArchivo _seleccion;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly IConfirmacionService _confirmacion;
    private readonly IAuthorizationService _authorization;
    private readonly ICurrentSession _session;

    private int? _gastoId;
    private int? _pagoGastoId;

    public ObservableCollection<AdjuntoDto> Items { get; } = new();

    [ObservableProperty]
    private bool _puedeModificar;

    public AdjuntosPanelViewModel(
        IAdjuntoService adjuntos,
        IServicioSeleccionArchivo seleccion,
        IServicioAperturaArchivo apertura,
        IConfirmacionService confirmacion,
        IAuthorizationService authorization,
        ICurrentSession session)
    {
        _adjuntos = adjuntos;
        _seleccion = seleccion;
        _apertura = apertura;
        _confirmacion = confirmacion;
        _authorization = authorization;
        _session = session;
    }

    public async Task InicializarAsync(int? gastoId, int? pagoGastoId)
    {
        _gastoId = gastoId;
        _pagoGastoId = pagoGastoId;

        var accion = gastoId is int ? Permisos.RegistrarGastos : Permisos.RegistrarPagos;
        PuedeModificar = _session.RolActual is RolUsuario rol && _authorization.TienePermiso(rol, accion);

        await RecargarAsync();
    }
```

por (mismo criterio que las propiedades `Puede*` de `ShellMainViewModel`, Task 14 — lectura directa de la sesión, sin pasar por `IAuthorizationService`; `IAuthorizationService` deja de ser una dependencia de este ViewModel):

```csharp
    private readonly IAdjuntoService _adjuntos;
    private readonly IServicioSeleccionArchivo _seleccion;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly IConfirmacionService _confirmacion;
    private readonly ICurrentSession _session;

    private int? _gastoId;
    private int? _pagoGastoId;

    public ObservableCollection<AdjuntoDto> Items { get; } = new();

    [ObservableProperty]
    private bool _puedeModificar;

    public AdjuntosPanelViewModel(
        IAdjuntoService adjuntos,
        IServicioSeleccionArchivo seleccion,
        IServicioAperturaArchivo apertura,
        IConfirmacionService confirmacion,
        ICurrentSession session)
    {
        _adjuntos = adjuntos;
        _seleccion = seleccion;
        _apertura = apertura;
        _confirmacion = confirmacion;
        _session = session;
    }

    public async Task InicializarAsync(int? gastoId, int? pagoGastoId)
    {
        _gastoId = gastoId;
        _pagoGastoId = pagoGastoId;

        var accion = gastoId is int ? Permisos.RegistrarGastos : Permisos.RegistrarPagos;
        PuedeModificar = _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(accion);

        await RecargarAsync();
    }
```

`using StockApp.Application.Authorization;` puede quedar (sigue haciendo falta por `Permisos.RegistrarGastos`/`Permisos.RegistrarPagos`).

Ahora hay que arreglar los 6 call sites que construyen `AdjuntosPanelViewModel(...)` con 6 argumentos (el quinto era `IAuthorizationService`). En estos 5 archivos, quitar la línea `new Mock<IAuthorizationService>().Object,` (queda con 5 argumentos):

```bash
sd '\n\s*new Mock<IAuthorizationService>\(\)\.Object,' '' \
  tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs \
  tests/StockApp.Presentation.Tests/ViewModels/Finanzas/CalendarioPagosViewModelTests.cs \
  tests/StockApp.Presentation.Tests/ViewModels/Finanzas/PagosGastoViewModelTests.cs \
  tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs
```

Verificar con `git diff` que en `GastoFormViewModelTests.cs` se quitaron **las dos** ocurrencias (hay dos construcciones de `AdjuntosPanelViewModel` en ese archivo). Si el `sd` con `\n` no matchea en tu entorno, editar las 5 ubicaciones a mano — son 5 líneas idénticas de contenido, buscar `new Mock<IAuthorizationService>().Object,` con `grep -n` en cada archivo.

`tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs` necesita un cambio más profundo porque usa `_authorization` en aserciones, no solo en el constructor. Reemplazar:

```csharp
    private readonly Mock<IAdjuntoService> _adjuntos = new();
    private readonly Mock<IServicioSeleccionArchivo> _seleccion = new();
    private readonly Mock<IServicioAperturaArchivo> _apertura = new();
    private readonly Mock<IConfirmacionService> _confirmacion = new();
    private readonly Mock<IAuthorizationService> _authorization = new();
    private readonly Mock<ICurrentSession> _session = new();
    private readonly AdjuntosPanelViewModel _vm;

    public AdjuntosPanelViewModelTests()
    {
        _vm = new AdjuntosPanelViewModel(
            _adjuntos.Object, _seleccion.Object, _apertura.Object, _confirmacion.Object,
            _authorization.Object, _session.Object);
    }
```

por:

```csharp
    private readonly Mock<IAdjuntoService> _adjuntos = new();
    private readonly Mock<IServicioSeleccionArchivo> _seleccion = new();
    private readonly Mock<IServicioAperturaArchivo> _apertura = new();
    private readonly Mock<IConfirmacionService> _confirmacion = new();
    private readonly Mock<ICurrentSession> _session = new();
    private readonly AdjuntosPanelViewModel _vm;

    public AdjuntosPanelViewModelTests()
    {
        _vm = new AdjuntosPanelViewModel(
            _adjuntos.Object, _seleccion.Object, _apertura.Object, _confirmacion.Object,
            _session.Object);
    }
```

Y los 3 tests de permisos:

```csharp
    [Fact]
    public async Task InicializarAsync_PanelDeGasto_ConPermisoRegistrarGastos_HabilitaPuedeModificar()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _authorization.Setup(a => a.TienePermiso(RolUsuario.Operador, Permisos.RegistrarGastos)).Returns(true);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());

        await _vm.InicializarAsync(gastoId: 5, pagoGastoId: null);

        Assert.True(_vm.PuedeModificar);
        _authorization.Verify(a => a.TienePermiso(RolUsuario.Operador, Permisos.RegistrarGastos), Times.Once);
    }

    [Fact]
    public async Task InicializarAsync_PanelDeGasto_SinPermisoRegistrarGastos_DeshabilitaPuedeModificar()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _authorization.Setup(a => a.TienePermiso(RolUsuario.Operador, Permisos.RegistrarGastos)).Returns(false);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());

        await _vm.InicializarAsync(gastoId: 5, pagoGastoId: null);

        Assert.False(_vm.PuedeModificar);
    }

    [Fact]
    public async Task InicializarAsync_PanelDePago_ConPermisoRegistrarPagos_HabilitaPuedeModificar()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _authorization.Setup(a => a.TienePermiso(RolUsuario.Operador, Permisos.RegistrarPagos)).Returns(true);
        _adjuntos.Setup(a => a.ListarPorPagoAsync(8)).ReturnsAsync(new List<AdjuntoDto>());

        await _vm.InicializarAsync(gastoId: null, pagoGastoId: 8);

        Assert.True(_vm.PuedeModificar);
        _authorization.Verify(a => a.TienePermiso(RolUsuario.Operador, Permisos.RegistrarPagos), Times.Once);
    }
```

por:

```csharp
    [Fact]
    public async Task InicializarAsync_PanelDeGasto_ConPermisoRegistrarGastos_HabilitaPuedeModificar()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _session.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { Permisos.RegistrarGastos });
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());

        await _vm.InicializarAsync(gastoId: 5, pagoGastoId: null);

        Assert.True(_vm.PuedeModificar);
    }

    [Fact]
    public async Task InicializarAsync_PanelDeGasto_SinPermisoRegistrarGastos_DeshabilitaPuedeModificar()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());

        await _vm.InicializarAsync(gastoId: 5, pagoGastoId: null);

        Assert.False(_vm.PuedeModificar);
    }

    [Fact]
    public async Task InicializarAsync_PanelDePago_ConPermisoRegistrarPagos_HabilitaPuedeModificar()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _session.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { Permisos.RegistrarPagos });
        _adjuntos.Setup(a => a.ListarPorPagoAsync(8)).ReturnsAsync(new List<AdjuntoDto>());

        await _vm.InicializarAsync(gastoId: null, pagoGastoId: 8);

        Assert.True(_vm.PuedeModificar);
    }
```

Correr: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~AdjuntosPanelViewModelTests|FullyQualifiedName~IngresoPorFacturaViewModelTests|FullyQualifiedName~CalendarioPagosViewModelTests|FullyQualifiedName~PagosGastoViewModelTests|FullyQualifiedName~GastoFormViewModelTests"`
Expected: todos PASS.

- [ ] **Step 12: Eliminar los tests del overload viejo**

En `tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs`, eliminar los métodos de test que llaman al overload `Verificar(RolUsuario?, string)` o a `TienePermiso` (los que quedaron de antes de la Task 5: `Admin_PuedeEjecutarCualquierAccion`, `Operador_PuedeEjecutarAccionesOperativas`, `Operador_NoPuedeEjecutarAccionesDeAdmin`, `Operador_NoTieneGestionarTablasMaestras_LanzaUnauthorized`, `SinSesion_CualquierAccionLanzaExcepcion`, y los 4 métodos con prefijo `TienePermiso_`). Dejar únicamente los agregados en la Task 5 (prefijo `VerificarConSesion_`) más `PermisosEstructuralesAdmin_ContieneExactamenteLosCuatroDocumentados`, `PermisosConfigurables_TieneLos11RestantesYNoIntersecaConLosEstructurales` y `PermisosInicialesOperador_TieneExactamenteLos9DeAccionesOperadorEnOrden`.

- [ ] **Step 13: Correr la suite completa de los 4 proyectos tocados**

Run: `dotnet test tests/StockApp.Application.Tests && dotnet test tests/StockApp.Api.Tests && dotnet test tests/StockApp.Presentation.Tests`
Expected: todo verde. `StockApp.ApiClient` no se tocó en esta task (el `IAuthorizationService` que usa `App.axaml.cs`/`AdjuntosPanelViewModel` es la implementación de `Application`, no un cliente HTTP), pero correr `dotnet test tests/StockApp.ApiClient.Tests` igual para descartar cualquier regresión no anticipada.

- [ ] **Step 14: Commit**

```bash
git add src/StockApp.Api/Auth/PermisoRequirement.cs \
        src/StockApp.Api/Auth/PermisoAuthorizationHandler.cs \
        src/StockApp.Api/Program.cs \
        src/StockApp.Application/Authorization/IAuthorizationService.cs \
        src/StockApp.Application/Authorization/AuthorizationService.cs \
        src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs \
        tests/StockApp.Api.Tests/Auth/PermisoAuthorizationHandlerTests.cs \
        tests/StockApp.Api.Tests/Auth/PermisosEndpointGuardTests.cs \
        tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/CalendarioPagosViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/PagosGastoViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs
git commit -m "feat(permisos): PermisoAuthorizationHandler reemplaza el RequireClaim fijo por rol"
```

---

### Task 8: Middleware `PoblarPermisosMiddleware`

**Files:**
- Modify: `src/StockApp.Api/Program.cs` (inserta el middleware entre `UseAuthorization()` y `MapAuthEndpoints()`)
- Test: `tests/StockApp.Api.Tests/Auth/PoblarPermisosMiddlewareTests.cs`

**Interfaces:**
- Consumes: `ICurrentSession.EstablecerPermisos` (Task 4), `IProveedorPermisos.ObtenerAsync` (Task 3), `AuthorizationService.Verificar(ICurrentSession, string)` (Task 5, ya consume `PermisosActuales`).

**Decisión de alcance del test (no está en el spec explícitamente):** el spec pide verificar "una ruta anónima no dispara ningún SELECT" — contar queries SQL reales requeriría interceptar el `DbCommand` de Npgsql, fuera de alcance de este plan. En su lugar, el test de ruta anónima confirma el comportamiento observable equivalente y suficiente para el propósito de la regla: que una ruta sin autenticación sigue funcionando sin excepciones tras insertar el middleware (si el middleware intentara leer un claim inexistente sin guardas, reventaría ahí). La prueba de que el middleware puebla `PermisosActuales` se hace de punta a punta contra la barrera Application real (Task 5) — es la forma más fuerte de probarlo: si el middleware no poblara la sesión, `AuthorizationService.Verificar` fallaría con `PermisosActuales` vacío para CUALQUIER Operador, sin importar lo que tenga en `PermisoUsuario`.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Api.Tests/Auth/PoblarPermisosMiddlewareTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class PoblarPermisosMiddlewareTests : ApiTestBase
{
    public PoblarPermisosMiddlewareTests(ApiFactory factory) : base(factory) { }

    private async Task<(string Token, int UsuarioId)> CrearOperadorConTokenAsync(string nombre)
    {
        await using var ctx = Factory.CrearContexto();
        var usuario = await DatosDePrueba.SeedUsuarioAsync(ctx, nombre, "Secreta123!", RolUsuario.Operador);
        var token = Factory.Services.GetRequiredService<IJwtTokenService>()
            .GenerarToken(usuario.Id, RolUsuario.Operador);
        return (token, usuario.Id);
    }

    [Fact]
    public async Task OperadorConElPermisoEnPermisoUsuario_LlegaAlEndpointYObtiene201()
    {
        // POST /tareas exige GestionarTareas (permiso configurable). Si el middleware no
        // poblara ICurrentSession.PermisosActuales antes de que TareaService.AltaAsync llame
        // a AuthorizationService.Verificar, esto tendría que dar 403 sin importar la fila real
        // en PermisoUsuario — el hecho de que dé 201 prueba que el middleware corrió.
        var (token, usuarioId) = await CrearOperadorConTokenAsync("operador.conpermiso");
        var proveedor = Factory.Services.GetRequiredService<IProveedorPermisos>();
        await proveedor.GuardarAsync(usuarioId, new[] { Permisos.GestionarTareas });

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/tareas", new { Titulo = "Tarea de prueba" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task OperadorSinElPermisoEnPermisoUsuario_Recibe403()
    {
        var (token, _) = await CrearOperadorConTokenAsync("operador.sinpermiso");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/tareas", new { Titulo = "Tarea de prueba" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RutaAnonima_SigueFuncionandoSinExcepciones()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login",
            new { NombreUsuario = "no-existe", Contrasena = "no-importa" });

        // 401 esperado (credenciales inválidas) — lo que importa es que el pipeline no reviente
        // con una excepción no controlada al pasar por el middleware nuevo sin usuario autenticado.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

Verificar que `DatosDePrueba.SeedUsuarioAsync` (usado en `UsuariosEndpointTests.cs`) devuelva el `Usuario` creado (con `.Id`) — si su firma real difiere, ajustar la llamada de `CrearOperadorConTokenAsync` a la firma real leyendo `tests/StockApp.Api.Tests/Fixtures/DatosDePrueba.cs` antes de escribir el test.

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~PoblarPermisosMiddlewareTests`
Expected: FALLA — `OperadorConElPermisoEnPermisoUsuario_LlegaAlEndpointYObtiene201` da 403 en vez de 201 (el middleware todavía no existe, `PermisosActuales` queda vacío para todo Operador).

- [ ] **Step 3: Insertar el middleware en `Program.cs`**

En `src/StockApp.Api/Program.cs`, entre `app.UseAuthorization();` (línea ~574) y `app.MapGet("/", ...)` / `app.MapAuthEndpoints();` (líneas ~576-578), insertar:

```csharp
app.UseAuthentication();
app.UseAuthorization();

// Middleware de permisos (spec 2026-08-10): resuelve una sola vez por request, DESPUÉS de que
// el usuario esté autenticado y autorizado a nivel de policy (PermisoAuthorizationHandler, que
// corre DENTRO de UseAuthorization() y por lo tanto ANTES que este middleware), ANTES de que
// cualquier endpoint (y por lo tanto cualquier servicio de Application) se ejecute. Es el único
// punto de I/O asíncrono de todo este diseño del lado Application — permite que
// AuthorizationService.Verificar siga siendo sincrónico. Para requests sin sesión (login,
// licencia) no hace nada. Cuando el handler ya resolvió el permiso de la policy del endpoint,
// esto pega al mismo cache de IProveedorPermisos — cache-hit, no un segundo SELECT.
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var usuarioIdClaim = context.User.FindFirst(StockAppClaimTypes.UsuarioId)?.Value;
        if (usuarioIdClaim is not null && int.TryParse(usuarioIdClaim, out var usuarioId))
        {
            var session = context.RequestServices.GetRequiredService<ICurrentSession>();
            var proveedor = context.RequestServices.GetRequiredService<IProveedorPermisos>();
            session.EstablecerPermisos(await proveedor.ObtenerAsync(usuarioId));
        }
    }
    await next(context);
});

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "StockApp.Api" }));

app.MapAuthEndpoints();
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~PoblarPermisosMiddlewareTests`
Expected: 3 tests PASS.

- [ ] **Step 5: Correr la suite completa de Api**

Run: `dotnet test tests/StockApp.Api.Tests`
Expected: todo verde.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api/Program.cs \
        tests/StockApp.Api.Tests/Auth/PoblarPermisosMiddlewareTests.cs
git commit -m "feat(permisos): middleware que puebla ICurrentSession.PermisosActuales por request"
```

---

### Task 9: `GET /auth/permisos` + cliente ApiClient + refresco tras login

**Files:**
- Modify: `src/StockApp.Api/Endpoints/AuthEndpoints.cs`
- Modify: `src/StockApp.Application/Auth/IAuthService.cs`
- Modify: `src/StockApp.ApiClient/AuthApiClient.cs`
- Modify: `src/StockApp.Presentation/ViewModels/LoginViewModel.cs`
- Create: `src/StockApp.Presentation/Services/RefrescoPermisos.cs`
- Test: `tests/StockApp.Api.Tests/AuthEndpointPermisosTests.cs`
- Test: `tests/StockApp.ApiClient.Tests/AuthApiClientPermisosTests.cs`
- Test: `tests/StockApp.Presentation.Tests/Services/RefrescoPermisosTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/LoginViewModelTests.cs` (agregar)

**Interfaces:**
- Consumes: `ICurrentSession.RolActual`/`.UsuarioActual` (existente), `IProveedorPermisos.ObtenerAsync` (Task 3), `AuthorizationService.PermisosConfigurables` (Task 5), `ApiSession.EstablecerPermisos` (Task 4).
- Produces: `IAuthService.ObtenerPermisosPropiosAsync()` → `Task<IReadOnlySet<string>>`; `AuthEndpoints` gana `GET /permisos` bajo el grupo `/auth`; `StockApp.Presentation.Services.RefrescoPermisos.DispararBestEffortAsync(Func<Task> operacion, string origen)` → `Task` (helper compartido, consumido también por las Tasks 13, 14 y 15 — la firma de arriba es EXACTA y no puede variar entre esas tasks).

**Decisión de diseño — helper compartido `RefrescoPermisos` (pre-flight, corrección C):** cuatro puntos del sistema de permisos disparan una operación asíncrona en modo "mejor esfuerzo" (nunca bloquean su flujo disparador, nunca propagan la excepción): el refresco tras login (esta task), el refresco al navegar entre secciones (Task 14), el refresco al cambiar de usuario seleccionado en el panel de permisos (Task 13) y el aviso de 403 (Task 15). Los cuatro son estructuralmente el mismo bloque `try { await operacion(); } catch (Exception) { /* best-effort */ }` — se factoriza acá, en la PRIMERA task que lo necesita, para que las tres siguientes lo consuman en vez de repetirlo. Vive en `StockApp.Presentation/Services/` (no en `StockApp.ApiClient` ni `StockApp.Application`) porque los cuatro consumidores están en `StockApp.Presentation` (`LoginViewModel`, `ShellMainViewModel`, `PanelPermisosViewModel` y `App.axaml.cs` — este último también vive en el proyecto Presentation, no en ApiClient) — no hace falta ninguna referencia nueva entre proyectos. Es una clase `static` (no una interfaz con DI): su comportamiento (capturar y loguear, nunca lanzar) no necesita sustituirse en tests — lo que varía por test es la función que envuelve, no el wrapper. Devuelve el `Task` que envuelve (nunca lanza) para que un llamador que necesite sincronización determinista en tests pueda guardarlo en un campo `internal Task` y hacerle `await` — mismo patrón ya establecido en el repo (`ShellViewModel._tareaActualizacion`, `ProductoListViewModel._tareaDebounce`) — ver Task 13, Step 1 (Corrección A).

**Decisión de diseño — logging, no silencio (pre-flight, corrección C):** "mejor esfuerzo" no debería significar "invisible". `StockApp.Presentation` no tiene `ILogger<T>` inyectado en ningún ViewModel — el único mecanismo de logging del proyecto es `Program.LogFatal(string origen, Exception ex)` (`src/StockApp.Presentation/Program.cs`), que ya escribe a `crash.log` y ya se usa hoy para excepciones "atrapadas pero dignas de rastro" (ver `Dispatcher.UIThread.UnhandledException` en `App.axaml.cs`: "se loguea a crash.log y se informa al usuario" — un caso manejado, no un crash real). `Program` es una clase top-level `internal` en el namespace `StockApp.Presentation`; `RefrescoPermisos` puede llamar a `Program.LogFatal` directo, mismo assembly, sin necesitar `InternalsVisibleTo` adicional.

- [ ] **Step 1: Escribir el test del helper que falla**

`tests/StockApp.Presentation.Tests/Services/RefrescoPermisosTests.cs`:

```csharp
using StockApp.Presentation.Services;
using Xunit;

namespace StockApp.Presentation.Tests.Services;

public class RefrescoPermisosTests
{
    [Fact]
    public async Task DispararBestEffortAsync_OperacionExitosa_LaEjecuta()
    {
        var ejecutada = false;

        await RefrescoPermisos.DispararBestEffortAsync(
            () => { ejecutada = true; return Task.CompletedTask; }, "test");

        Assert.True(ejecutada);
    }

    [Fact]
    public async Task DispararBestEffortAsync_OperacionLanzaSincronicamente_NoPropagaLaExcepcion()
    {
        var ex = await Record.ExceptionAsync(() =>
            RefrescoPermisos.DispararBestEffortAsync(
                () => throw new InvalidOperationException("boom"), "test"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task DispararBestEffortAsync_OperacionLanzaAsincronicamente_NoPropagaLaExcepcion()
    {
        var ex = await Record.ExceptionAsync(() =>
            RefrescoPermisos.DispararBestEffortAsync(
                async () => { await Task.Yield(); throw new InvalidOperationException("boom"); }, "test"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task DispararBestEffortAsync_DevuelveElTaskQueEnvuelveLaOperacion_ParaSincronizacionDeterministaEnTests()
    {
        // Este es el contrato que consume PanelPermisosViewModel (Task 13, corrección A): el
        // Task devuelto completa DESPUÉS de que la operación (exitosa o no) terminó, nunca
        // antes — es lo que permite awaitarlo desde un test sin Task.Delay.
        var ordenDeEjecucion = new List<string>();

        var tarea = RefrescoPermisos.DispararBestEffortAsync(async () =>
        {
            await Task.Yield();
            ordenDeEjecucion.Add("operacion");
        }, "test");
        await tarea;
        ordenDeEjecucion.Add("despues-del-await");

        Assert.Equal(new[] { "operacion", "despues-del-await" }, ordenDeEjecucion);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter FullyQualifiedName~RefrescoPermisosTests`
Expected: FALLA de compilación — `RefrescoPermisos` no existe.

- [ ] **Step 3: Implementar el helper**

`src/StockApp.Presentation/Services/RefrescoPermisos.cs`:

```csharp
using System;
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Ejecuta una operación asíncrona en modo "mejor esfuerzo" (spec 2026-08-10): nunca propaga
/// la excepción, la deja registrada en crash.log vía Program.LogFatal con el origen indicado
/// — "mejor esfuerzo" no significa "invisible". Consumido por los cuatro puntos del sistema de
/// permisos que refrescan el cache local sin poder bloquear ni interrumpir el flujo que los
/// dispara: login (LoginViewModel, esta task), navegación entre secciones (ShellMainViewModel,
/// Task 14), cambio de usuario seleccionado en el panel de permisos (PanelPermisosViewModel,
/// Task 13) y el aviso de 403 (App.axaml.cs, Task 15).
///
/// Devuelve el Task que envuelve la operación (nunca lanza): quien lo llame puede ignorarlo
/// (fire-and-forget puro, `_ = RefrescoPermisos.DispararBestEffortAsync(...)`) o guardarlo en
/// un campo `internal Task` para que un test lo awaite de forma determinista — mismo patrón que
/// ShellViewModel._tareaActualizacion / ProductoListViewModel._tareaDebounce ya usan en este
/// repo para el mismo problema (sincronizar un test con trabajo fire-and-forget sin Task.Delay).
/// </summary>
public static class RefrescoPermisos
{
    public static async Task DispararBestEffortAsync(Func<Task> operacion, string origen)
    {
        try
        {
            await operacion();
        }
        catch (Exception ex)
        {
            StockApp.Presentation.Program.LogFatal(origen, ex);
        }
    }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter FullyQualifiedName~RefrescoPermisosTests`
Expected: 4 tests PASS.

**Decisión de diseño (evita romper `AuthApiClientTests.cs` existente):** el spec dice "el desktop lo consulta al loguearse, con un GET /auth/permisos inmediato". Encadenar esa llamada DENTRO de `AuthApiClient.LoginAsync` rompería los ~10 tests existentes de ese archivo (su `FakeHttpHandler` devuelve una única respuesta canned sin importar la URL — un segundo request dentro del mismo método recibiría el body de login, no el de permisos, y fallaría al deserializar). En cambio, el refresco se dispara desde `LoginViewModel.EntrarAsync`, inmediatamente después de un login exitoso y antes de navegar — mismo efecto observable (permisos poblados antes de que se dibuje el shell), sin tocar `AuthApiClient.LoginAsync` ni sus tests.

- [ ] **Step 5: Escribir los tests de Api que fallan**

`tests/StockApp.Api.Tests/AuthEndpointPermisosTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public record PermisosPropiosResponseTest(List<string> Permisos);

public class AuthEndpointPermisosTests : ApiTestBase
{
    public AuthEndpointPermisosTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    [Fact]
    public async Task GetPermisos_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/auth/permisos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPermisos_Admin_DevuelveLos11Configurables()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/auth/permisos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PermisosPropiosResponseTest>();
        Assert.Equal(11, body!.Permisos.Count);
        Assert.Contains(Permisos.VerFinanzas, body.Permisos);
        Assert.DoesNotContain(Permisos.GestionarUsuarios, body.Permisos);
    }

    [Fact]
    public async Task GetPermisos_Operador_DevuelveSoloLosConcedidos()
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.permisos", "Secreta123!", RolUsuario.Operador);
        var proveedor = Factory.Services.GetRequiredService<IProveedorPermisos>();
        await proveedor.GuardarAsync(operador.Id, new[] { Permisos.VerFinanzas, Permisos.GestionarProductos });
        var token = Factory.Services.GetRequiredService<IJwtTokenService>()
            .GenerarToken(operador.Id, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/auth/permisos");

        var body = await response.Content.ReadFromJsonAsync<PermisosPropiosResponseTest>();
        Assert.Equal(2, body!.Permisos.Count);
        Assert.Contains(Permisos.VerFinanzas, body.Permisos);
        Assert.Contains(Permisos.GestionarProductos, body.Permisos);
    }
}
```

- [ ] **Step 6: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~AuthEndpointPermisosTests`
Expected: FALLA — `GET /auth/permisos` no existe (404).

- [ ] **Step 7: Agregar el endpoint**

En `src/StockApp.Api/Endpoints/AuthEndpoints.cs`, agregar el record de response (junto a los existentes) y el endpoint dentro de `MapAuthEndpoints`:

```csharp
public record PermisosPropiosResponse(IReadOnlyList<string> Permisos);
```

```csharp
        group.MapGet("/permisos", async (ICurrentSession session, IProveedorPermisos proveedor) =>
        {
            if (session.RolActual == RolUsuario.Admin)
                return Results.Ok(new PermisosPropiosResponse(AuthorizationService.PermisosConfigurables));

            var permisos = await proveedor.ObtenerAsync(session.UsuarioActual!.Id);
            return Results.Ok(new PermisosPropiosResponse(permisos.ToList()));
        }).RequireAuthorization();
```

`AuthEndpoints.cs` hoy importa `StockApp.Api.Auth`, `StockApp.Application.Auth`, `StockApp.Application.Interfaces` y `StockApp.Domain.Enums`, pero no `StockApp.Application.Authorization` (para `AuthorizationService`) ni `System.Linq` (para `.ToList()` sobre el `IReadOnlySet<string>` que devuelve `IProveedorPermisos.ObtenerAsync`) — agregar ambos al tope.

- [ ] **Step 8: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~AuthEndpointPermisosTests`
Expected: 3 tests PASS.

- [ ] **Step 9: Escribir el test del cliente que falla**

`tests/StockApp.ApiClient.Tests/AuthApiClientPermisosTests.cs`:

```csharp
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient.Tests;

public class AuthApiClientPermisosTests
{
    [Fact]
    public async Task ObtenerPermisosPropiosAsync_GETAuthPermisos_DevuelveElSetYPueblaLaSesion()
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "operador", RolUsuario.Operador, null), "tok-1");
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { permisos = new[] { "finanzas.ver" } }));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        var permisos = await client.ObtenerPermisosPropiosAsync();

        Assert.Equal("/auth/permisos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("finanzas.ver", permisos);
        Assert.Contains("finanzas.ver", session.PermisosActuales);
    }
}
```

- [ ] **Step 10: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter FullyQualifiedName~AuthApiClientPermisosTests`
Expected: FALLA de compilación — `ObtenerPermisosPropiosAsync` no existe en `IAuthService`/`AuthApiClient`.

- [ ] **Step 11: Ampliar `IAuthService`**

En `src/StockApp.Application/Auth/IAuthService.cs`:

```csharp
namespace StockApp.Application.Auth;

/// <summary>Contrato de autenticación. Permite mockear AuthService en tests de Presentation.</summary>
public interface IAuthService
{
    Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena);
    Task LogoutAsync();

    /// <summary>Permisos configurables efectivos del usuario autenticado (spec 2026-08-10),
    /// vía GET /auth/permisos. Como efecto colateral, puebla ICurrentSession.PermisosActuales —
    /// mismo criterio que LoginAsync puebla la sesión completa.</summary>
    Task<IReadOnlySet<string>> ObtenerPermisosPropiosAsync();
}
```

- [ ] **Step 12: Implementar en `AuthApiClient`**

En `src/StockApp.ApiClient/AuthApiClient.cs`, agregar el wire record y el método:

```csharp
internal sealed record PermisosPropiosWire(List<string> Permisos);
```

```csharp
    public async Task<IReadOnlySet<string>> ObtenerPermisosPropiosAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("auth/permisos"));
        await ApiErrores.AsegurarExitoAsync(response);

        var body = await response.Content.ReadFromJsonAsync<PermisosPropiosWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al consultar permisos.");

        IReadOnlySet<string> permisos = new HashSet<string>(body.Permisos);
        _session.EstablecerPermisos(permisos);
        return permisos;
    }
```

- [ ] **Step 13: Correr el test del cliente y verificar que pasa**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter FullyQualifiedName~AuthApiClientPermisosTests`
Expected: 1 test PASS.

- [ ] **Step 14: Refrescar los permisos tras un login exitoso**

En `src/StockApp.Presentation/ViewModels/LoginViewModel.cs`, en `EntrarAsync`, reemplazar:

```csharp
            if (resultado.Exitoso)
            {
                if (SoloAccesoLimitado)
                    _shell.MostrarAccesoLimitado();
                else
                    _shell.MostrarContenidoPrincipal();
            }
```

por:

```csharp
            if (resultado.Exitoso)
            {
                // Poblar los permisos configurables ANTES de navegar (spec 2026-08-10, decisión
                // 7): el gating del menú (ShellMainViewModel.Puede*, Task 14) lee
                // ApiSession.PermisosActuales desde el primer render. Best-effort vía el helper
                // compartido (RefrescoPermisos, Steps 1-4 de esta task): si la consulta falla,
                // no bloquea el login — el menú arranca sin permisos configurables hasta el
                // próximo refresh (navegación, Task 14, o el manejo de 403, Task 15).
                await RefrescoPermisos.DispararBestEffortAsync(
                    () => _authService.ObtenerPermisosPropiosAsync(), nameof(LoginViewModel));

                if (SoloAccesoLimitado)
                    _shell.MostrarAccesoLimitado();
                else
                    _shell.MostrarContenidoPrincipal();
            }
```

Agregar `using StockApp.Presentation.Services;` al tope de `LoginViewModel.cs` si falta (para `RefrescoPermisos`). `ObtenerPermisosPropiosAsync()` devuelve `Task<IReadOnlySet<string>>`, no `Task` — `Func<Task>` acepta el lambda `() => _authService.ObtenerPermisosPropiosAsync()` igual porque `Task<T>` es un `Task` (covarianza de retorno implícita del compilador para delegados `Func<Task>`, el valor de retorno del `Task<T>` simplemente se descarta).

- [ ] **Step 15: Agregar el test de Presentation**

`LoginViewModelTests.cs` usa un `ShellViewModel` REAL construido por el helper `CrearShellFake()` (no un mock — `ShellViewModel` es una clase concreta), y el helper `Crear(LoginResult resultado)` devuelve `(LoginViewModel vm, Mock<IAuthService> authMock, ShellViewModel shell)`. Los tests existentes de login exitoso verifican la navegación con `Assert.IsType<ShellMainViewModel>(shell.CurrentViewModel)` (ver `Login_Exitoso_NavegaAContenidoPrincipal`), no con un `Verify` sobre un mock de shell — mismo patrón a seguir acá.

Agregar a `tests/StockApp.Presentation.Tests/ViewModels/LoginViewModelTests.cs`:

```csharp
    [Fact]
    public async Task Login_Exitoso_ConsultaLosPermisosPropiosAntesDeNavegar()
    {
        var (vm, authMock, shell) = Crear(LoginResult.Ok());
        vm.NombreUsuario = "admin";
        vm.Contrasena    = "secreto";

        await vm.EntrarCommand.ExecuteAsync(null);

        authMock.Verify(a => a.ObtenerPermisosPropiosAsync(), Times.Once);
        Assert.IsType<ShellMainViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task Login_ObtenerPermisosPropiosFalla_IgualNavegaAContenidoPrincipal()
    {
        var (vm, authMock, shell) = Crear(LoginResult.Ok());
        authMock.Setup(a => a.ObtenerPermisosPropiosAsync())
            .ThrowsAsync(new ServidorNoDisponibleException(new HttpRequestException()));
        vm.NombreUsuario = "admin";
        vm.Contrasena    = "secreto";

        await vm.EntrarCommand.ExecuteAsync(null);

        Assert.IsType<ShellMainViewModel>(shell.CurrentViewModel);
    }
```

Agregar `using StockApp.Domain.Exceptions;` al tope del archivo si falta (para `ServidorNoDisponibleException`).

- [ ] **Step 16: Correr la suite completa de los proyectos tocados**

Run: `dotnet test tests/StockApp.Api.Tests && dotnet test tests/StockApp.ApiClient.Tests && dotnet test tests/StockApp.Presentation.Tests`
Expected: todo verde.

- [ ] **Step 17: Commit**

```bash
git add src/StockApp.Api/Endpoints/AuthEndpoints.cs \
        src/StockApp.Application/Auth/IAuthService.cs \
        src/StockApp.ApiClient/AuthApiClient.cs \
        src/StockApp.Presentation/ViewModels/LoginViewModel.cs \
        src/StockApp.Presentation/Services/RefrescoPermisos.cs \
        tests/StockApp.Api.Tests/AuthEndpointPermisosTests.cs \
        tests/StockApp.ApiClient.Tests/AuthApiClientPermisosTests.cs \
        tests/StockApp.Presentation.Tests/Services/RefrescoPermisosTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/LoginViewModelTests.cs
git commit -m "feat(permisos): GET /auth/permisos, refresco tras login y helper RefrescoPermisos"
```

---

### Task 10: `GET/PUT /usuarios/{id}/permisos` + validaciones + auditoría

**Files:**
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs`
- Modify: `src/StockApp.Application/Auth/IUsuarioService.cs`
- Modify: `src/StockApp.Application/Auth/UsuarioService.cs`
- Modify: `src/StockApp.Api/Endpoints/UsuariosEndpoints.cs`
- Modify: `src/StockApp.ApiClient/UsuarioApiClient.cs`
- Test: `tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs` (agregar + ajustar `Crear`)
- Test: `tests/StockApp.Api.Tests/UsuariosEndpointTests.cs` (agregar)
- Test: `tests/StockApp.ApiClient.Tests/UsuarioApiClientTests.cs` (agregar)

**Interfaces:**
- Consumes: `IProveedorPermisos.ObtenerAsync`/`.GuardarAsync` (Task 3), `AuthorizationService.PermisosConfigurables` (Task 5), `IAuthorizationService.Verificar(ICurrentSession, string)` (Task 5/6a).
- Produces: `IUsuarioService.ObtenerPermisosAsync(int usuarioId)` → `Task<IReadOnlyList<string>>`; `.GuardarPermisosAsync(int usuarioId, IReadOnlyList<string> permisos)` → `Task`; `AccionAuditada.ModificacionPermisosUsuario = 51`.

- [ ] **Step 1: Agregar el valor de auditoría**

En `src/StockApp.Domain/Enums/AccionAuditada.cs`, append-only al final:

```csharp
    // ── Permisos por operador (append-only a partir de 51) ───────────────────
    ModificacionPermisosUsuario = 51,
```

- [ ] **Step 2: Escribir los tests de `UsuarioService` que fallan**

Primero, actualizar el helper `Crear` de `tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs` (después de la Task 6a, ya usa `session.Object` en los `Verificar`) para agregar el mock de `IProveedorPermisos`. Reemplazar:

```csharp
    private static (UsuarioService service,
                    Mock<IUsuarioRepository> repoMock,
                    Mock<IPasswordHasher> hasherMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock,
                    Mock<IRevocadorTokens> revocadorMock)
        Crear(RolUsuario rolSesion = RolUsuario.Admin, int idSesion = 1)
    {
        var repo       = new Mock<IUsuarioRepository>();
        var hasher     = new Mock<IPasswordHasher>();
        var session    = new Mock<ICurrentSession>();
        var auth       = new Mock<IAuthSvc>();
        var audit      = new Mock<IAuditLogger>();
        var revocador  = new Mock<IRevocadorTokens>();

        session.Setup(s => s.RolActual).Returns(rolSesion);
        session.Setup(s => s.UsuarioActual).Returns(SesionAdmin(idSesion));
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("$2a$12$hashed");

        // Admin: Verificar no lanza
        if (rolSesion == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(session.Object, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(session.Object, Permisos.GestionarUsuarios))
                .Throws<UnauthorizedAccessException>();

        var svc = new UsuarioService(
            repo.Object, hasher.Object, session.Object, auth.Object, audit.Object, revocador.Object);
        return (svc, repo, hasher, session, auth, audit, revocador);
    }
```

por:

```csharp
    private static (UsuarioService service,
                    Mock<IUsuarioRepository> repoMock,
                    Mock<IPasswordHasher> hasherMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock,
                    Mock<IRevocadorTokens> revocadorMock,
                    Mock<IProveedorPermisos> permisosMock)
        Crear(RolUsuario rolSesion = RolUsuario.Admin, int idSesion = 1)
    {
        var repo       = new Mock<IUsuarioRepository>();
        var hasher     = new Mock<IPasswordHasher>();
        var session    = new Mock<ICurrentSession>();
        var auth       = new Mock<IAuthSvc>();
        var audit      = new Mock<IAuditLogger>();
        var revocador  = new Mock<IRevocadorTokens>();
        var permisos   = new Mock<IProveedorPermisos>();

        session.Setup(s => s.RolActual).Returns(rolSesion);
        session.Setup(s => s.UsuarioActual).Returns(SesionAdmin(idSesion));
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("$2a$12$hashed");

        // Admin: Verificar no lanza
        if (rolSesion == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(session.Object, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(session.Object, Permisos.GestionarUsuarios))
                .Throws<UnauthorizedAccessException>();

        var svc = new UsuarioService(
            repo.Object, hasher.Object, session.Object, auth.Object, audit.Object, revocador.Object,
            permisos.Object);
        return (svc, repo, hasher, session, auth, audit, revocador, permisos);
    }
```

Esto rompe la firma de TODOS los llamados existentes a `Crear(...)` en el archivo (desestructuran una tupla de 7 elementos, ahora son 8). Ajustar cada `var (svc, repo, ...) = Crear(...);` existente agregando el octavo elemento (ej. `var (svc, repo, hasher, session, _, audit, _, _) = Crear();` según qué necesite cada test — usar `_` para los que no se usan, siguiendo el mismo estilo ya presente en el archivo).

Agregar al final de la clase (antes del cierre):

```csharp
    [Fact]
    public async Task ObtenerPermisosAsync_UsuarioOperador_DevuelveLosDelProveedor()
    {
        var (svc, repo, _, _, _, _, _, permisos) = Crear();
        var operador = new Usuario { Id = 9, Rol = RolUsuario.Operador, NombreUsuario = "op", HashContrasena = "h", FechaAlta = DateTime.UtcNow };
        repo.Setup(r => r.ObtenerPorIdAsync(9)).ReturnsAsync(operador);
        permisos.Setup(p => p.ObtenerAsync(9))
            .ReturnsAsync((IReadOnlySet<string>)new HashSet<string> { Permisos.VerFinanzas });

        var resultado = await svc.ObtenerPermisosAsync(9);

        Assert.Contains(Permisos.VerFinanzas, resultado);
    }

    [Fact]
    public async Task ObtenerPermisosAsync_UsuarioAdmin_DevuelveLos11ConfigurablesSinConsultarElProveedor()
    {
        var (svc, repo, _, _, _, _, _, permisos) = Crear();
        var admin = new Usuario { Id = 10, Rol = RolUsuario.Admin, NombreUsuario = "adm2", HashContrasena = "h", FechaAlta = DateTime.UtcNow };
        repo.Setup(r => r.ObtenerPorIdAsync(10)).ReturnsAsync(admin);

        var resultado = await svc.ObtenerPermisosAsync(10);

        Assert.Equal(11, resultado.Count);
        permisos.Verify(p => p.ObtenerAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerPermisosAsync_UsuarioInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(404)).ReturnsAsync((Usuario?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.ObtenerPermisosAsync(404));
    }

    [Fact]
    public async Task GuardarPermisosAsync_UsuarioOperador_LlamaAGuardarAsyncYAuditar()
    {
        var (svc, repo, _, _, _, audit, _, permisos) = Crear();
        var operador = new Usuario { Id = 9, Rol = RolUsuario.Operador, NombreUsuario = "op", HashContrasena = "h", FechaAlta = DateTime.UtcNow };
        repo.Setup(r => r.ObtenerPorIdAsync(9)).ReturnsAsync(operador);

        await svc.GuardarPermisosAsync(9, new[] { Permisos.VerFinanzas, Permisos.GestionarProductos });

        permisos.Verify(p => p.GuardarAsync(9,
            It.Is<IReadOnlyCollection<string>>(c => c.Contains(Permisos.VerFinanzas) && c.Contains(Permisos.GestionarProductos))),
            Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionPermisosUsuario, "Usuario", 9, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task GuardarPermisosAsync_UsuarioAdmin_Rechaza400SinLlamarAlProveedor()
    {
        var (svc, repo, _, _, _, _, _, permisos) = Crear();
        var admin = new Usuario { Id = 10, Rol = RolUsuario.Admin, NombreUsuario = "adm2", HashContrasena = "h", FechaAlta = DateTime.UtcNow };
        repo.Setup(r => r.ObtenerPorIdAsync(10)).ReturnsAsync(admin);

        await Assert.ThrowsAsync<ArgumentException>(() => svc.GuardarPermisosAsync(10, new[] { Permisos.VerFinanzas }));
        permisos.Verify(p => p.GuardarAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
    }

    [Fact]
    public async Task GuardarPermisosAsync_PermisoFueraDeWhitelist_Rechaza400()
    {
        var (svc, repo, _, _, _, _, _, permisos) = Crear();
        var operador = new Usuario { Id = 9, Rol = RolUsuario.Operador, NombreUsuario = "op", HashContrasena = "h", FechaAlta = DateTime.UtcNow };
        repo.Setup(r => r.ObtenerPorIdAsync(9)).ReturnsAsync(operador);

        // GestionarUsuarios es estructural — nunca debería poder colarse por esta vía, ni
        // siquiera como intento de un cliente viejo o manipulado.
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.GuardarPermisosAsync(9, new[] { Permisos.GestionarUsuarios }));
        permisos.Verify(p => p.GuardarAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
    }

    [Fact]
    public async Task GuardarPermisosAsync_UsuarioInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _, _, _, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(404)).ReturnsAsync((Usuario?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.GuardarPermisosAsync(404, new[] { Permisos.VerFinanzas }));
    }
```

Agregar `using StockApp.Application.Authorization;` al tope si no está (ya está, por `Permisos`).

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~UsuarioServiceTests`
Expected: FALLA de compilación — `ObtenerPermisosAsync`/`GuardarPermisosAsync` no existen en `IUsuarioService`, y el constructor de `UsuarioService` no acepta `IProveedorPermisos`.

- [ ] **Step 4: Ampliar `IUsuarioService`**

En `src/StockApp.Application/Auth/IUsuarioService.cs`:

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;

/// <summary>Contrato del ABM de usuarios. Permite mockear UsuarioService en tests de Presentation.</summary>
public interface IUsuarioService
{
    Task<int> AltaUsuarioAsync(string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol);
    Task BajaLogicaAsync(int usuarioId);
    Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol);
    Task CambiarContrasenaAsync(int usuarioId, string nuevaContrasenaPlan, string? contrasenaActualPlan = null);
    Task<IReadOnlyList<UsuarioDto>> ListarAsync();

    /// <summary>Permisos configurables actuales del usuario (spec 2026-08-10). Para un Admin,
    /// devuelve los 11 configurables completos (siempre los tiene). Requiere GestionarUsuarios.</summary>
    Task<IReadOnlyList<string>> ObtenerPermisosAsync(int usuarioId);

    /// <summary>Reemplaza el set de permisos configurables del usuario. 400 si el usuario es
    /// Admin, 400 si algún permiso no está en la whitelist de configurables. Requiere
    /// GestionarUsuarios. Registra AccionAuditada.ModificacionPermisosUsuario.</summary>
    Task GuardarPermisosAsync(int usuarioId, IReadOnlyList<string> permisos);
}
```

- [ ] **Step 5: Implementar en `UsuarioService`**

En `src/StockApp.Application/Auth/UsuarioService.cs`, agregar el campo, ampliar el constructor y agregar los dos métodos al final de la clase:

```csharp
    private readonly IUsuarioRepository    _repo;
    private readonly IPasswordHasher       _hasher;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger          _audit;
    private readonly IRevocadorTokens      _revocador;
    private readonly IProveedorPermisos    _permisos;

    public UsuarioService(
        IUsuarioRepository    repo,
        IPasswordHasher       hasher,
        ICurrentSession       session,
        IAuthorizationService auth,
        IAuditLogger          audit,
        IRevocadorTokens      revocador,
        IProveedorPermisos    permisos)
    {
        _repo      = repo;
        _hasher    = hasher;
        _session   = session;
        _auth      = auth;
        _audit     = audit;
        _revocador = revocador;
        _permisos  = permisos;
    }
```

```csharp
    public async Task<IReadOnlyList<string>> ObtenerPermisosAsync(int usuarioId)
    {
        _auth.Verificar(_session, Permisos.GestionarUsuarios);

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new EntidadNoEncontradaException($"Usuario {usuarioId} no encontrado.");

        // Admin siempre tiene los 11 configurables — no hace falta consultar el proveedor
        // (y de hecho no debería haber filas: nunca se le escriben, spec decisión 3).
        if (usuario.Rol == RolUsuario.Admin)
            return AuthorizationService.PermisosConfigurables;

        var permisos = await _permisos.ObtenerAsync(usuarioId);
        return permisos.ToList();
    }

    public async Task GuardarPermisosAsync(int usuarioId, IReadOnlyList<string> permisos)
    {
        _auth.Verificar(_session, Permisos.GestionarUsuarios);

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new EntidadNoEncontradaException($"Usuario {usuarioId} no encontrado.");

        // El servidor no confía en que el cliente deshabilite el panel de permisos para Admin
        // (spec, endpoint de administración): lo valida también del lado seguro.
        if (usuario.Rol == RolUsuario.Admin)
            throw new ArgumentException(
                "No se pueden configurar permisos para un usuario Admin: tiene acceso total.");

        // Defensa contra un cliente viejo o manipulado intentando colar un permiso estructural
        // (ej. GestionarUsuarios) — nunca deberían estar en la whitelist de configurables.
        var fueraDeWhitelist = permisos.Where(p => !AuthorizationService.PermisosConfigurables.Contains(p)).ToList();
        if (fueraDeWhitelist.Count > 0)
            throw new ArgumentException(
                $"Los siguientes permisos no son configurables: {string.Join(", ", fueraDeWhitelist)}.");

        await _permisos.GuardarAsync(usuarioId, permisos);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionPermisosUsuario,
            "Usuario", usuarioId,
            $"Permisos actualizados: {string.Join(", ", permisos)}");
    }
```

Agregar `using StockApp.Application.Authorization;` y `using StockApp.Application.Interfaces;` al tope si faltan (ya deberían estar, por `Permisos`/`IAuthorizationService`/`ICurrentSession`).

- [ ] **Step 6: Registrar `IProveedorPermisos` como dependencia disponible (ya lo está desde la Task 3)**

No hace falta ningún registro nuevo en `Program.cs`: `IProveedorPermisos` ya está registrado Singleton desde la Task 3, y `AddScoped<IUsuarioService, UsuarioService>()` ya existente lo resuelve automáticamente por el constructor ampliado.

- [ ] **Step 7: Correr los tests de Application y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~UsuarioServiceTests`
Expected: todos PASS (los de antes + los 7 nuevos).

- [ ] **Step 8: Escribir los tests de Api que fallan**

Agregar a `tests/StockApp.Api.Tests/UsuariosEndpointTests.cs` (al final de la clase, siguiendo el estilo `TokenAdmin()`/`TokenOperador()` ya presente en el archivo):

```csharp
    // ── GET/PUT /usuarios/{id}/permisos (spec 2026-08-10) ────────────────────

    [Fact]
    public async Task GetPermisos_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/usuarios/1/permisos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetPermisos_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/usuarios/1/permisos");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutPermisos_Admin_GuardaYQuedaLegibleEnGet()
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.putpermisos", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var put = await client.PutAsJsonAsync($"/usuarios/{operador.Id}/permisos",
            new { Permisos = new[] { "finanzas.ver", "catalogo.productos" } });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var get = await client.GetAsync($"/usuarios/{operador.Id}/permisos");
        var body = await get.Content.ReadFromJsonAsync<PermisosPropiosResponseTest>();
        Assert.Equal(2, body!.Permisos.Count);
        Assert.Contains("finanzas.ver", body.Permisos);
        Assert.Contains("catalogo.productos", body.Permisos);
    }

    [Fact]
    public async Task PutPermisos_UsuarioObjetivoEsAdmin_Devuelve400()
    {
        await using var ctx = Factory.CrearContexto();
        var otroAdmin = await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.destino", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/usuarios/{otroAdmin.Id}/permisos",
            new { Permisos = new[] { "finanzas.ver" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutPermisos_PermisoFueraDeWhitelist_Devuelve400()
    {
        await using var ctx = Factory.CrearContexto();
        var operador = await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.whitelist", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/usuarios/{operador.Id}/permisos",
            new { Permisos = new[] { "usuarios.gestionar" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
```

`PermisosPropiosResponseTest` ya existe (definido en `AuthEndpointPermisosTests.cs`, Task 9, mismo namespace `StockApp.Api.Tests`) — no hace falta redeclararlo.

- [ ] **Step 9: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~UsuariosEndpointTests`
Expected: FALLA — `GET/PUT /usuarios/{id}/permisos` no existen (404).

- [ ] **Step 10: Agregar los endpoints**

En `src/StockApp.Api/Endpoints/UsuariosEndpoints.cs`, agregar el record de request (junto a los existentes) y las dos rutas dentro de `MapUsuariosEndpoints`:

```csharp
public record GuardarPermisosRequest(string[]? Permisos);
public record PermisosUsuarioResponse(IReadOnlyList<string> Permisos);
```

```csharp
        group.MapGet("/{id:int}/permisos", async (int id, IUsuarioService usuarios) =>
            Results.Ok(new PermisosUsuarioResponse(await usuarios.ObtenerPermisosAsync(id))));

        group.MapPut("/{id:int}/permisos", async (int id, GuardarPermisosRequest request, IUsuarioService usuarios) =>
        {
            await usuarios.GuardarPermisosAsync(id, request.Permisos ?? Array.Empty<string>());
            return Results.Ok();
        });
```

(Ambas quedan bajo el `group` ya protegido por `.RequireAuthorization(Permisos.GestionarUsuarios)` a nivel de grupo, línea 24 — no hace falta repetirlo.)

- [ ] **Step 11: Correr los tests de Api y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~UsuariosEndpointTests`
Expected: todos PASS.

- [ ] **Step 12: Agregar los métodos al cliente ApiClient**

En `src/StockApp.ApiClient/UsuarioApiClient.cs`, agregar los wire records y los dos métodos:

```csharp
internal sealed record GuardarPermisosBody(IReadOnlyList<string> Permisos);
internal sealed record PermisosUsuarioWire(List<string> Permisos);
```

```csharp
    public async Task<IReadOnlyList<string>> ObtenerPermisosAsync(int usuarioId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"usuarios/{usuarioId}/permisos"));
        await ApiErrores.AsegurarExitoAsync(response);

        var body = await response.Content.ReadFromJsonAsync<PermisosUsuarioWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al consultar permisos.");
        return body.Permisos;
    }

    public async Task GuardarPermisosAsync(int usuarioId, IReadOnlyList<string> permisos)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"usuarios/{usuarioId}/permisos", new GuardarPermisosBody(permisos)));
        await ApiErrores.AsegurarExitoAsync(response);
    }
```

Y agregar la firma a `IUsuarioService` (ya hecha en el Step 4 — `UsuarioApiClient` la implementa acá).

- [ ] **Step 13: Escribir y correr los tests del cliente**

Agregar a `tests/StockApp.ApiClient.Tests/UsuarioApiClientTests.cs`:

```csharp
    [Fact]
    public async Task ObtenerPermisos_GETUsuariosIdPermisos_DevuelveLaLista()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { permisos = new[] { "finanzas.ver" } }));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        var permisos = await client.ObtenerPermisosAsync(9);

        Assert.Equal("/usuarios/9/permisos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("finanzas.ver", permisos);
    }

    [Fact]
    public async Task GuardarPermisos_PUTUsuariosIdPermisos()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        await client.GuardarPermisosAsync(9, new[] { "finanzas.ver", "catalogo.productos" });

        Assert.Equal("/usuarios/9/permisos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"finanzas.ver\"", fake.UltimoBody);
        Assert.Contains("\"catalogo.productos\"", fake.UltimoBody);
    }

    [Fact]
    public async Task GuardarPermisos_400UsuarioAdmin_LanzaArgumentException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "No se pueden configurar permisos para un usuario Admin: tiene acceso total."));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.GuardarPermisosAsync(1, new[] { "finanzas.ver" }));
    }
```

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter FullyQualifiedName~UsuarioApiClientTests`
Expected: todos PASS.

- [ ] **Step 14: Correr la suite completa de los 4 proyectos tocados**

Run: `dotnet test tests/StockApp.Application.Tests && dotnet test tests/StockApp.Api.Tests && dotnet test tests/StockApp.ApiClient.Tests`
Expected: todo verde.

- [ ] **Step 15: Commit**

```bash
git add src/StockApp.Domain/Enums/AccionAuditada.cs \
        src/StockApp.Application/Auth/IUsuarioService.cs \
        src/StockApp.Application/Auth/UsuarioService.cs \
        src/StockApp.Api/Endpoints/UsuariosEndpoints.cs \
        src/StockApp.ApiClient/UsuarioApiClient.cs \
        tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs \
        tests/StockApp.Api.Tests/UsuariosEndpointTests.cs \
        tests/StockApp.ApiClient.Tests/UsuarioApiClientTests.cs
git commit -m "feat(permisos): endpoints GET/PUT /usuarios/{id}/permisos con validaciones y auditoria"
```

---

### Task 11: `UsuarioService.AltaUsuarioAsync` siembra `PermisosInicialesOperador`

**Files:**
- Modify: `src/StockApp.Application/Auth/UsuarioService.cs`
- Test: `tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs`

**Interfaces:**
- Consumes: `AuthorizationService.PermisosInicialesOperador` (Task 5), `IProveedorPermisos.GuardarAsync` (Task 3, ya inyectado desde la Task 10).

- [ ] **Step 1: Escribir los tests que fallan**

Agregar a `tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs`:

```csharp
    [Fact]
    public async Task AltaUsuario_Operador_SiembraPermisosInicialesOperador()
    {
        var (svc, repo, _, _, _, _, _, permisos) = Crear();
        repo.Setup(r => r.AgregarAsync(It.IsAny<Usuario>())).ReturnsAsync(50);

        await svc.AltaUsuarioAsync("operador.nuevo", "Nuevo Operador", "pwd12345", RolUsuario.Operador);

        permisos.Verify(p => p.GuardarAsync(50,
            It.Is<IReadOnlyCollection<string>>(c => c.SequenceEqual(AuthorizationService.PermisosInicialesOperador))),
            Times.Once);
    }

    [Fact]
    public async Task AltaUsuario_Admin_NoSiembraNingunPermiso()
    {
        var (svc, repo, _, _, _, _, _, permisos) = Crear();
        repo.Setup(r => r.AgregarAsync(It.IsAny<Usuario>())).ReturnsAsync(51);

        await svc.AltaUsuarioAsync("admin.nuevo", "Nuevo Admin", "pwd12345", RolUsuario.Admin);

        permisos.Verify(p => p.GuardarAsync(It.IsAny<int>(), It.IsAny<IReadOnlyCollection<string>>()), Times.Never);
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~UsuarioServiceTests`
Expected: FALLA — `permisos.Verify` no recibe ninguna llamada (`AltaUsuarioAsync` todavía no siembra nada).

- [ ] **Step 3: Sembrar los permisos iniciales en `AltaUsuarioAsync`**

En `src/StockApp.Application/Auth/UsuarioService.cs`, en `AltaUsuarioAsync`, reemplazar:

```csharp
        var id = await _repo.AgregarAsync(nuevo);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaUsuario,
            "Usuario", id,
            $"Alta de '{nombreNormalizado}' con rol {rol}");

        return id;
```

por:

```csharp
        var id = await _repo.AgregarAsync(nuevo);

        // Plantilla de arranque (spec decisión 3): sin este paso, todo Operador nuevo
        // arrancaría con cero permisos configurables — fail-closed correcto pero inútil en la
        // práctica. Admin nunca siembra nada acá: sus permisos son siempre todos, sin filas.
        if (rol == RolUsuario.Operador)
            await _permisos.GuardarAsync(id, AuthorizationService.PermisosInicialesOperador);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaUsuario,
            "Usuario", id,
            $"Alta de '{nombreNormalizado}' con rol {rol}");

        return id;
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~UsuarioServiceTests`
Expected: todos PASS.

- [ ] **Step 5: Correr la suite completa de Application**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: todo verde.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application/Auth/UsuarioService.cs \
        tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs
git commit -m "feat(permisos): AltaUsuarioAsync siembra PermisosInicialesOperador para Operador nuevo"
```

---

### Task 12: Pantalla de administración de usuarios (ABM que hoy no existe en el desktop)

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Administracion/UsuariosAdminViewModel.cs`
- Create: `src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml`
- Create: `src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs` (registro DI)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Administracion/UsuariosAdminViewModelTests.cs`

**Interfaces:**
- Consumes: `IUsuarioService` (existente, ampliado en Task 10: `ListarAsync`, `AltaUsuarioAsync`, `BajaLogicaAsync`, `CambiarRolAsync`, `CambiarContrasenaAsync`); `IConfirmacionService.PreguntarAsync`/`.InformarAsync` (existente).
- Produces: `UsuariosAdminViewModel` con `Items`, `UsuarioSeleccionado`, `EsAdminSeleccionado`, comandos `AltaCommand`/`BajaCommand`/`CambiarRolCommand`/`CambiarContrasenaCommand` — consumido por `PanelPermisosViewModel` en la Task 13 vía `UsuarioSeleccionado`.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Presentation.Tests/ViewModels/Administracion/UsuariosAdminViewModelTests.cs`:

```csharp
using Moq;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Administracion;

public class UsuariosAdminViewModelTests
{
    private static (UsuariosAdminViewModel vm, Mock<IUsuarioService> svc, Mock<IConfirmacionService> confirm) Crear()
    {
        var svc = new Mock<IUsuarioService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        var vm = new UsuariosAdminViewModel(svc.Object, confirm.Object);
        return (vm, svc, confirm);
    }

    private static UsuarioDto Dto(int id, RolUsuario rol) =>
        new(id, $"usuario{id}", null, rol, true, DateTime.UtcNow);

    [Fact]
    public async Task CargarAsync_PueblaItemsDesdeElServicio()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto> { Dto(1, RolUsuario.Admin), Dto(2, RolUsuario.Operador) });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public void UsuarioSeleccionado_Admin_EsAdminSeleccionadoEsTrue()
    {
        var (vm, _, _) = Crear();

        vm.UsuarioSeleccionado = Dto(1, RolUsuario.Admin);

        Assert.True(vm.EsAdminSeleccionado);
    }

    [Fact]
    public void UsuarioSeleccionado_Operador_EsAdminSeleccionadoEsFalse()
    {
        var (vm, _, _) = Crear();

        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        Assert.False(vm.EsAdminSeleccionado);
    }

    [Fact]
    public async Task AltaAsync_LlamaAlServicioConLosCamposCargados_YRecarga()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaUsuarioAsync("nuevo", "Nombre Completo", "pwd12345", RolUsuario.Operador))
            .ReturnsAsync(9);
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto>());
        vm.NuevoNombreUsuario = "nuevo";
        vm.NuevoNombreCompleto = "Nombre Completo";
        vm.NuevaContrasenaPlan = "pwd12345";
        vm.NuevoRol = RolUsuario.Operador;

        await vm.AltaCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaUsuarioAsync("nuevo", "Nombre Completo", "pwd12345", RolUsuario.Operador), Times.Once);
        svc.Verify(s => s.ListarAsync(), Times.Once);
        Assert.Equal(string.Empty, vm.NuevoNombreUsuario);
    }

    [Fact]
    public async Task AltaAsync_NombreDuplicado_MuestraMensajeErrorSinRecargar()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaUsuarioAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<RolUsuario>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe un usuario con ese nombre."));
        vm.NuevoNombreUsuario = "repetido";
        vm.NuevaContrasenaPlan = "pwd12345";

        await vm.AltaCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe un usuario con ese nombre.", vm.MensajeError);
        svc.Verify(s => s.ListarAsync(), Times.Never);
    }

    [Fact]
    public async Task BajaAsync_ConConfirmacion_LlamaAlServicioYRecarga()
    {
        var (vm, svc, confirm) = Crear();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto>());
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        await vm.BajaCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svc.Verify(s => s.BajaLogicaAsync(2), Times.Once);
    }

    [Fact]
    public async Task BajaAsync_SinSeleccion_NoLlamaAlServicio()
    {
        var (vm, svc, _) = Crear();

        await vm.BajaCommand.ExecuteAsync(null);

        svc.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CambiarRolAsync_LlamaAlServicioConElUsuarioSeleccionadoYRecarga()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto>());
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        await vm.CambiarRolCommand.ExecuteAsync(RolUsuario.Admin);

        svc.Verify(s => s.CambiarRolAsync(2, RolUsuario.Admin), Times.Once);
    }

    [Fact]
    public async Task CambiarContrasenaAsync_LlamaAlServicioConLaContrasenaCargadaYLimpiaElCampo()
    {
        var (vm, svc, _) = Crear();
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);
        vm.NuevaContrasenaParaSeleccionado = "otraClave123";

        await vm.CambiarContrasenaCommand.ExecuteAsync(null);

        svc.Verify(s => s.CambiarContrasenaAsync(2, "otraClave123", null), Times.Once);
        Assert.Equal(string.Empty, vm.NuevaContrasenaParaSeleccionado);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter FullyQualifiedName~UsuariosAdminViewModelTests`
Expected: FALLA de compilación — `UsuariosAdminViewModel` no existe.

- [ ] **Step 3: Implementar el ViewModel**

`src/StockApp.Presentation/ViewModels/Administracion/UsuariosAdminViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Pantalla de administración de usuarios (spec 2026-08-10): ABM completo — listar, alta, baja
/// lógica, cambio de rol, cambio de contraseña — que hasta esta task no existía en el desktop
/// pese a que el backend ya lo soportaba desde Fase 2b. Layout de dos columnas: esta clase
/// gobierna la izquierda (lista + alta); el panel de permisos de la derecha es
/// PanelPermisosViewModel (Task 13), que lee UsuarioSeleccionado de esta misma instancia.
/// </summary>
public partial class UsuariosAdminViewModel : ViewModelBase
{
    private readonly IUsuarioService _usuarios;
    private readonly IConfirmacionService _confirmacion;

    public ObservableCollection<UsuarioDto> Items { get; } = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    [NotifyCanExecuteChangedFor(nameof(CambiarRolCommand))]
    [NotifyCanExecuteChangedFor(nameof(CambiarContrasenaCommand))]
    [NotifyPropertyChangedFor(nameof(EsAdminSeleccionado))]
    private UsuarioDto? _usuarioSeleccionado;

    /// <summary>Gatea el panel de permisos de la Task 13: "Acceso total" deshabilitado para Admin.</summary>
    public bool EsAdminSeleccionado => UsuarioSeleccionado?.Rol == RolUsuario.Admin;

    [ObservableProperty] private string _nuevoNombreUsuario = string.Empty;
    [ObservableProperty] private string? _nuevoNombreCompleto;
    [ObservableProperty] private string _nuevaContrasenaPlan = string.Empty;
    [ObservableProperty] private RolUsuario _nuevoRol = RolUsuario.Operador;
    [ObservableProperty] private string? _mensajeError;
    [ObservableProperty] private string _nuevaContrasenaParaSeleccionado = string.Empty;

    /// <summary>Fuente del ComboBox de rol en el alta (View, Step 5) — evita hardcodear los
    /// dos valores del enum en el XAML.</summary>
    public IReadOnlyList<RolUsuario> RolesDisponibles { get; } = Enum.GetValues<RolUsuario>();

    public UsuariosAdminViewModel(IUsuarioService usuarios, IConfirmacionService confirmacion)
    {
        _usuarios = usuarios;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        var lista = await _usuarios.ListarAsync();
        Items.Clear();
        foreach (var u in lista)
            Items.Add(u);
    }

    [RelayCommand]
    private async Task AltaAsync()
    {
        MensajeError = null;
        try
        {
            await _usuarios.AltaUsuarioAsync(NuevoNombreUsuario, NuevoNombreCompleto, NuevaContrasenaPlan, NuevoRol);
            NuevoNombreUsuario = string.Empty;
            NuevoNombreCompleto = null;
            NuevaContrasenaPlan = string.Empty;
            NuevoRol = RolUsuario.Operador;
            await CargarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    private bool PuedeOperarSobreSeleccionado() => UsuarioSeleccionado is not null;

    [RelayCommand(CanExecute = nameof(PuedeOperarSobreSeleccionado))]
    private async Task BajaAsync()
    {
        if (UsuarioSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja a \"{UsuarioSeleccionado.NombreUsuario}\"?");
        if (!confirmar) return;

        try
        {
            await _usuarios.BajaLogicaAsync(UsuarioSeleccionado.Id);
            await CargarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeOperarSobreSeleccionado))]
    private async Task CambiarRolAsync(RolUsuario nuevoRol)
    {
        if (UsuarioSeleccionado is null) return;

        try
        {
            await _usuarios.CambiarRolAsync(UsuarioSeleccionado.Id, nuevoRol);
            await CargarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or ArgumentException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeOperarSobreSeleccionado))]
    private async Task CambiarContrasenaAsync()
    {
        if (UsuarioSeleccionado is null) return;

        try
        {
            // Reset administrativo (spec Auth §5.1): Admin cambia la de otro sin requerir la
            // contraseña actual — tercer argumento null a propósito, mismo criterio que ya
            // implementa UsuarioService.CambiarContrasenaAsync.
            await _usuarios.CambiarContrasenaAsync(UsuarioSeleccionado.Id, NuevaContrasenaParaSeleccionado, null);
            NuevaContrasenaParaSeleccionado = string.Empty;
            await _confirmacion.InformarAsync("Contraseña actualizada.");
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or ArgumentException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter FullyQualifiedName~UsuariosAdminViewModelTests`
Expected: 9 tests PASS.

- [ ] **Step 5: Crear la View**

`src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Administracion"
             xmlns:enums="using:StockApp.Domain.Enums"
             x:Class="StockApp.Presentation.Views.Administracion.UsuariosAdminView"
             x:DataType="vm:UsuariosAdminViewModel">
    <Grid ColumnDefinitions="360,*" Margin="16">
        <!-- Columna izquierda: lista + alta -->
        <DockPanel Grid.Column="0" Margin="0,0,16,0">
            <StackPanel DockPanel.Dock="Top" Spacing="8" Margin="0,0,0,12">
                <TextBlock Text="Nuevo usuario" FontWeight="Bold" />
                <TextBox Watermark="Nombre de usuario" Text="{Binding NuevoNombreUsuario}" />
                <TextBox Watermark="Nombre completo (opcional)" Text="{Binding NuevoNombreCompleto}" />
                <TextBox Watermark="Contraseña" PasswordChar="*" Text="{Binding NuevaContrasenaPlan}" />
                <ComboBox ItemsSource="{Binding RolesDisponibles}" SelectedItem="{Binding NuevoRol}" />
                <Button Content="Crear usuario" Command="{Binding AltaCommand}" />
                <TextBlock Text="{Binding MensajeError}" Foreground="Red"
                           IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />
            </StackPanel>
            <ListBox ItemsSource="{Binding Items}" SelectedItem="{Binding UsuarioSeleccionado}">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <TextBlock Text="{Binding NombreUsuario}" />
                            <TextBlock Text="{Binding Rol}" Opacity="0.7" />
                            <TextBlock Text="(inactivo)" Foreground="Gray"
                                       IsVisible="{Binding !Activo}" />
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </DockPanel>

        <!-- Columna derecha: acciones del seleccionado + panel de permisos (Task 13) -->
        <StackPanel Grid.Column="1" Spacing="12" IsEnabled="{Binding UsuarioSeleccionado, Converter={x:Static ObjectConverters.IsNotNull}}">
            <TextBlock Text="{Binding UsuarioSeleccionado.NombreUsuario}" FontWeight="Bold" FontSize="18" />
            <StackPanel Orientation="Horizontal" Spacing="8">
                <Button Content="Dar de baja" Command="{Binding BajaCommand}" />
                <Button Content="Hacer Admin" Command="{Binding CambiarRolCommand}"
                        CommandParameter="{x:Static enums:RolUsuario.Admin}"
                        IsVisible="{Binding !EsAdminSeleccionado}" />
                <Button Content="Hacer Operador" Command="{Binding CambiarRolCommand}"
                        CommandParameter="{x:Static enums:RolUsuario.Operador}"
                        IsVisible="{Binding EsAdminSeleccionado}" />
            </StackPanel>
            <StackPanel Orientation="Horizontal" Spacing="8">
                <TextBox Watermark="Nueva contraseña" PasswordChar="*" Width="220"
                         Text="{Binding NuevaContrasenaParaSeleccionado}" />
                <Button Content="Cambiar contraseña" Command="{Binding CambiarContrasenaCommand}" />
            </StackPanel>
            <!-- Panel de permisos: ver PanelPermisosView, insertado acá en la Task 13 -->
        </StackPanel>
    </Grid>
</UserControl>
```

- [ ] **Step 6: Crear el code-behind**

`src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Administracion;

namespace StockApp.Presentation.Views.Administracion;

public partial class UsuariosAdminView : UserControl
{
    public UsuariosAdminView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is UsuariosAdminViewModel vm)
                _ = vm.CargarAsync();
        };
    }
}
```

(Enganche vía `DataContextChanged`, mismo patrón ya establecido en el repo para que las Views de Avalonia carguen datos al activarse — ver `MantenimientoView.axaml.cs`.)

- [ ] **Step 7: Registrar en DI**

En `src/StockApp.Presentation/App.axaml.cs`, junto al registro de `MantenimientoViewModel` (línea ~303):

```csharp
        services.AddTransient<StockApp.Presentation.ViewModels.Administracion.UsuariosAdminViewModel>();
```

- [ ] **Step 8: Correr la suite completa de Presentation**

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: todo verde.

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Administracion/UsuariosAdminViewModel.cs \
        src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml \
        src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Administracion/UsuariosAdminViewModelTests.cs
git commit -m "feat(permisos): pantalla de administracion de usuarios (ABM completo en el desktop)"
```

---

### Task 13: Panel de permisos con checkboxes agrupados y compuestos

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Administracion/PanelPermisosViewModel.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Administracion/UsuariosAdminViewModel.cs` (constructor + propiedad `PanelPermisos`)
- Modify: `src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml` (inserta el panel)
- Modify: `src/StockApp.Presentation/App.axaml.cs` (registro DI)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Administracion/PanelPermisosViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Administracion/UsuariosAdminViewModelTests.cs` (ajustar `Crear`)

**Interfaces:**
- Consumes: `UsuariosAdminViewModel.UsuarioSeleccionado`/`.EsAdminSeleccionado` (Task 12); `IUsuarioService.ObtenerPermisosAsync`/`.GuardarPermisosAsync` (Task 10); `StockApp.Presentation.Services.RefrescoPermisos.DispararBestEffortAsync(Func<Task> operacion, string origen)` → `Task` (Task 9).
- Produces: `PanelPermisosViewModel` con 11 propiedades booleanas (`Permiso*`, una por permiso configurable), 3 propiedades compuestas (`Productos`, `GastosYFacturas`, `IngresosDeCaja`), `EsAdminSeleccionado`, `GuardarCommand`, método `Conectar(UsuariosAdminViewModel padre)`, campo `internal Task _tareaCarga`.

**Decisión de diseño 1 (deriva de la tabla de mapeo del spec, no está en el spec como código):** las 11 propiedades booleanas base (una por permiso) son la fuente de verdad; los checkboxes que comparten permiso (Libro caja/Control POA/Calendario de pagos/Gastos/Ingresos, todos atados a `PermisoVerFinanzas`) se bindean literalmente a la misma propiedad — tildar cualquiera tilda los demás sin lógica extra. Los checkboxes compuestos (`Productos`, `GastosYFacturas`, `IngresosDeCaja`) son propiedades calculadas que fijan 2-3 propiedades base a la vez; tildar `GastosYFacturas` enciende también `PermisoVerFinanzas` (efecto visible, spec §"Sobre los checkboxes compuestos"), pero destildarlo NO apaga `PermisoVerFinanzas` (podría seguir siendo necesario para Libro caja/Control POA/Calendario, que tienen su propio checkbox independiente atado a la misma propiedad).

**Decisión de diseño 2 (gap real de infraestructura, no del spec):** `StockApp.Presentation/ViewLocator.cs` resuelve cada View con `Activator.CreateInstance(type)` — **sin argumentos**. Ninguna View de este repo puede tener un constructor con parámetros. Por eso `PanelPermisosViewModel` NO puede recibir la instancia de `UsuariosAdminViewModel` en su propio constructor (eso obligaría a resolver la composición en el code-behind de la View, que no puede recibir dependencias) — en cambio, `PanelPermisosViewModel` se registra `AddTransient` sin dependencias circulares, `UsuariosAdminViewModel` lo recibe como tercer parámetro de SU constructor (DI lo resuelve solo, sin ciclos: `PanelPermisosViewModel` no depende de `UsuariosAdminViewModel` en su constructor), y `UsuariosAdminViewModel` llama a `panelPermisos.Conectar(this)` una vez construido. El code-behind de la View (Task 12, Step 6) no cambia ni una línea.

**Decisión de diseño 3 (pre-flight, correcciones A y B):** `AlCambiarSeleccion` dispara `CargarAsync()` en fire-and-forget cada vez que cambia `UsuarioSeleccionado` — dos problemas reales de la primera versión de este plan, corregidos acá:
- **Determinismo del test** (corrección A): un test que espera con `await Task.Delay(10)` a que termine un fire-and-forget es flaky por construcción — pasa en una máquina liviana y falla bajo CI cargado, sin que nadie entienda por qué al leer el fallo. El VM expone el `Task` en curso en un campo `internal Task _tareaCarga = Task.CompletedTask;` (visible para `StockApp.Presentation.Tests` vía el `InternalsVisibleTo` que ya declara `StockApp.Presentation.csproj`) — mismo patrón exacto que `ShellViewModel._tareaActualizacion` y `ProductoListViewModel._tareaDebounce` ya usan en este repo para el mismo problema. El test hace `await panel._tareaCarga;` en vez de esperar un tiempo fijo.
- **Excepción no observada** (corrección B): sin manejo, una falla de `_usuarios.ObtenerPermisosAsync(...)` (ej. `ServidorNoDisponibleException` de un `UsuarioApiClient` real) queda como excepción no observada — inconsistente con las Tasks 14 y 15, que sí tratan sus fire-and-forget como mejor esfuerzo. `AlCambiarSeleccion` envuelve la llamada con `RefrescoPermisos.DispararBestEffortAsync` (Task 9) — el mismo helper que consumen las Tasks 14 y 15, así que las cuatro instancias de este patrón en el sistema de permisos comparten una sola implementación. El `Task` que devuelve el helper (que nunca lanza) es el que se guarda en `_tareaCarga` — así el test sigue pudiendo esperar de forma determinista sin importar si la carga tuvo éxito o falló.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Presentation.Tests/ViewModels/Administracion/PanelPermisosViewModelTests.cs`:

```csharp
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Administracion;

public class PanelPermisosViewModelTests
{
    private static UsuarioDto Dto(int id, RolUsuario rol) => new(id, $"u{id}", null, rol, true, DateTime.UtcNow);

    private static (PanelPermisosViewModel panel, UsuariosAdminViewModel padre, Mock<IUsuarioService> svc) Crear()
    {
        var svc = new Mock<IUsuarioService>();
        var confirm = new Mock<StockApp.Presentation.Services.IConfirmacionService>();
        var panel = new PanelPermisosViewModel(svc.Object);
        var padre = new UsuariosAdminViewModel(svc.Object, confirm.Object, panel);
        return (panel, padre, svc);
    }

    [Fact]
    public async Task CambiarUsuarioSeleccionado_CargaLosPermisosDelNuevo()
    {
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(9))
            .ReturnsAsync(new List<string> { Permisos.VerFinanzas, Permisos.GestionarProductos });

        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        // AlCambiarSeleccion dispara CargarAsync en fire-and-forget vía RefrescoPermisos —
        // _tareaCarga expone ese Task para esperarlo de forma determinista (pre-flight,
        // corrección A: nada de Task.Delay).
        await panel._tareaCarga;

        Assert.True(panel.PermisoVerFinanzas);
        Assert.True(panel.PermisoGestionarProductos);
        Assert.False(panel.PermisoRegistrarGastos);
    }

    [Fact]
    public async Task CambiarUsuarioSeleccionado_ObtenerPermisosAsyncFalla_NoPropagaLaExcepcion()
    {
        // Corrección B del pre-flight: sin RefrescoPermisos, esto quedaba como excepción no
        // observada. _tareaCarga nunca lanza — es el contrato de RefrescoPermisos.DispararBestEffortAsync.
        var (panel, padre, svc) = Crear();
        svc.Setup(s => s.ObtenerPermisosAsync(9))
            .ThrowsAsync(new InvalidOperationException("el servidor no respondió"));

        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        var ex = await Record.ExceptionAsync(() => panel._tareaCarga);

        Assert.Null(ex);
    }

    [Fact]
    public async Task CargarAsync_UsuarioAdmin_DejaTodoEnFalseSinConsultarElServicio()
    {
        var (panel, padre, svc) = Crear();
        padre.UsuarioSeleccionado = Dto(1, RolUsuario.Admin);

        await panel.CargarAsync();

        Assert.False(panel.PermisoVerFinanzas);
        svc.Verify(s => s.ObtenerPermisosAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void GastosYFacturas_AlTildar_EnciendeRegistrarGastosRegistrarPagosYVerFinanzas()
    {
        var (panel, _, _) = Crear();

        panel.GastosYFacturas = true;

        Assert.True(panel.PermisoRegistrarGastos);
        Assert.True(panel.PermisoRegistrarPagos);
        Assert.True(panel.PermisoVerFinanzas);
    }

    [Fact]
    public void GastosYFacturas_AlDestildar_ApagaGastosYPagosPeroNoVerFinanzas()
    {
        var (panel, _, _) = Crear();
        panel.PermisoVerFinanzas = true; // ej. porque Libro caja lo necesita
        panel.GastosYFacturas = true;

        panel.GastosYFacturas = false;

        Assert.False(panel.PermisoRegistrarGastos);
        Assert.False(panel.PermisoRegistrarPagos);
        Assert.True(panel.PermisoVerFinanzas);
    }

    [Fact]
    public void IngresosDeCaja_AlTildar_EnciendeRegistrarIngresosYVerFinanzas()
    {
        var (panel, _, _) = Crear();

        panel.IngresosDeCaja = true;

        Assert.True(panel.PermisoRegistrarIngresos);
        Assert.True(panel.PermisoVerFinanzas);
    }

    [Fact]
    public void Productos_AlTildar_EnciendeGestionarProductosYRecalcularStockJuntos()
    {
        var (panel, _, _) = Crear();

        panel.Productos = true;

        Assert.True(panel.PermisoGestionarProductos);
        Assert.True(panel.PermisoRecalcularStock);
    }

    [Fact]
    public async Task GuardarAsync_EnviaSoloLosPermisosTildados()
    {
        var (panel, padre, svc) = Crear();
        padre.UsuarioSeleccionado = Dto(9, RolUsuario.Operador);
        panel.PermisoVerFinanzas = true;
        panel.PermisoGestionarTareas = true;

        await panel.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.GuardarPermisosAsync(9,
            It.Is<IReadOnlyList<string>>(l =>
                l.Contains(Permisos.VerFinanzas) && l.Contains(Permisos.GestionarTareas) && l.Count == 2)),
            Times.Once);
    }

    [Fact]
    public async Task GuardarAsync_SinUsuarioSeleccionado_NoLlamaAlServicio()
    {
        var (panel, _, svc) = Crear();

        await panel.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.GuardarPermisosAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter FullyQualifiedName~PanelPermisosViewModelTests`
Expected: FALLA de compilación — `PanelPermisosViewModel` no existe.

- [ ] **Step 3: Implementar el ViewModel**

`src/StockApp.Presentation/ViewModels/Administracion/PanelPermisosViewModel.cs`:

```csharp
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Panel de permisos de la columna derecha de UsuariosAdminView (spec 2026-08-10, Task 12).
/// 11 checkboxes agrupados por sección, algunos compuestos (tildan 2-3 permisos juntos) y
/// algunos compartidos (bindeados a la MISMA propiedad cuando dos pantallas usan el mismo
/// permiso — tildar uno tilda el otro en el acto, sin lógica extra que lo sincronice).
/// </summary>
public partial class PanelPermisosViewModel : ViewModelBase
{
    private readonly IUsuarioService _usuarios;

    /// <summary>Poblado por Conectar(), llamado una única vez desde el constructor de
    /// UsuariosAdminViewModel — ver Decisión de diseño 2 (ViewLocator exige Views sin
    /// argumentos, así que la composición se resuelve enteramente en el grafo de constructores
    /// de los ViewModels, nunca en el code-behind).</summary>
    private UsuariosAdminViewModel? _padre;

    /// <summary>Expone el fire-and-forget de AlCambiarSeleccion para que los tests lo esperen
    /// de forma determinista, sin Task.Delay (pre-flight, corrección A) — mismo patrón que
    /// ShellViewModel._tareaActualizacion / ProductoListViewModel._tareaDebounce.</summary>
    internal Task _tareaCarga = Task.CompletedTask;

    // ── Catálogo / Stock ───────────────────────────────────────────────────
    [ObservableProperty] private bool _permisoGestionarProductos;
    [ObservableProperty] private bool _permisoRecalcularStock;
    [ObservableProperty] private bool _permisoGestionarTablasMaestras;
    [ObservableProperty] private bool _permisoRegistrarMovimientos;

    // ── Finanzas ───────────────────────────────────────────────────────────
    [ObservableProperty] private bool _permisoVerFinanzas;
    [ObservableProperty] private bool _permisoGestionarMaestrosFinanzas;
    [ObservableProperty] private bool _permisoRegistrarGastos;
    [ObservableProperty] private bool _permisoRegistrarPagos;
    [ObservableProperty] private bool _permisoRegistrarIngresos;

    // ── Tareas / Reportes ──────────────────────────────────────────────────
    [ObservableProperty] private bool _permisoGestionarTareas;
    [ObservableProperty] private bool _permisoVerReportes;

    /// <summary>Deshabilita el panel entero cuando el usuario seleccionado es Admin (leyenda
    /// "Acceso total" en la View) — proxy de UsuariosAdminViewModel.EsAdminSeleccionado. False
    /// antes de Conectar() (no debería observarse: Conectar corre en el constructor del padre,
    /// antes de que la View pueda bindear nada).</summary>
    public bool EsAdminSeleccionado => _padre?.EsAdminSeleccionado ?? false;

    /// <summary>Checkbox compuesto: Productos → GestionarProductos + RecalcularStock juntos.</summary>
    public bool Productos
    {
        get => PermisoGestionarProductos && PermisoRecalcularStock;
        set
        {
            PermisoGestionarProductos = value;
            PermisoRecalcularStock = value;
            OnPropertyChanged(nameof(Productos));
        }
    }

    /// <summary>Checkbox compuesto: Gastos y facturas → VerFinanzas + RegistrarGastos +
    /// RegistrarPagos. Tildarlo enciende también VerFinanzas (efecto visible, spec); destildarlo
    /// NO apaga VerFinanzas (Libro caja/Control POA/Calendario pueden seguir necesitándolo).</summary>
    public bool GastosYFacturas
    {
        get => PermisoRegistrarGastos && PermisoRegistrarPagos;
        set
        {
            PermisoRegistrarGastos = value;
            PermisoRegistrarPagos = value;
            if (value) PermisoVerFinanzas = true;
            OnPropertyChanged(nameof(GastosYFacturas));
        }
    }

    /// <summary>Checkbox compuesto: Ingresos de caja → VerFinanzas + RegistrarIngresos.</summary>
    public bool IngresosDeCaja
    {
        get => PermisoRegistrarIngresos;
        set
        {
            PermisoRegistrarIngresos = value;
            if (value) PermisoVerFinanzas = true;
            OnPropertyChanged(nameof(IngresosDeCaja));
        }
    }

    public PanelPermisosViewModel(IUsuarioService usuarios)
    {
        _usuarios = usuarios;
    }

    /// <summary>Conecta este panel con el UsuariosAdminViewModel que lo hostea. Llamado UNA
    /// VEZ desde el constructor de UsuariosAdminViewModel (Step 5) — nunca desde DI directa:
    /// PanelPermisosViewModel no puede recibir a UsuariosAdminViewModel en su propio
    /// constructor sin crear una dependencia circular en el grafo de DI (ver Decisión de
    /// diseño 2).</summary>
    public void Conectar(UsuariosAdminViewModel padre)
    {
        _padre = padre;
        _padre.PropertyChanged += AlCambiarSeleccion;
    }

    private void AlCambiarSeleccion(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UsuariosAdminViewModel.UsuarioSeleccionado)) return;

        OnPropertyChanged(nameof(EsAdminSeleccionado));
        // Mejor esfuerzo (pre-flight, corrección B): sin esto, una falla de
        // ObtenerPermisosAsync (ej. ServidorNoDisponibleException) quedaba como excepción no
        // observada. El Task envolvente (nunca lanza) se guarda en _tareaCarga para que los
        // tests lo esperen de forma determinista (corrección A).
        _tareaCarga = RefrescoPermisos.DispararBestEffortAsync(CargarAsync, nameof(PanelPermisosViewModel));
    }

    public async Task CargarAsync()
    {
        if (_padre?.UsuarioSeleccionado is null || _padre.EsAdminSeleccionado)
        {
            LimpiarTodo();
            return;
        }

        var permisos = await _usuarios.ObtenerPermisosAsync(_padre.UsuarioSeleccionado.Id);
        PermisoGestionarProductos       = permisos.Contains(Permisos.GestionarProductos);
        PermisoRecalcularStock          = permisos.Contains(Permisos.RecalcularStock);
        PermisoGestionarTablasMaestras  = permisos.Contains(Permisos.GestionarTablasMaestras);
        PermisoRegistrarMovimientos     = permisos.Contains(Permisos.RegistrarMovimientos);
        PermisoVerFinanzas              = permisos.Contains(Permisos.VerFinanzas);
        PermisoGestionarMaestrosFinanzas = permisos.Contains(Permisos.GestionarMaestrosFinanzas);
        PermisoRegistrarGastos          = permisos.Contains(Permisos.RegistrarGastos);
        PermisoRegistrarPagos           = permisos.Contains(Permisos.RegistrarPagos);
        PermisoRegistrarIngresos        = permisos.Contains(Permisos.RegistrarIngresos);
        PermisoGestionarTareas          = permisos.Contains(Permisos.GestionarTareas);
        PermisoVerReportes              = permisos.Contains(Permisos.VerReportes);
    }

    private void LimpiarTodo()
    {
        PermisoGestionarProductos = false;
        PermisoRecalcularStock = false;
        PermisoGestionarTablasMaestras = false;
        PermisoRegistrarMovimientos = false;
        PermisoVerFinanzas = false;
        PermisoGestionarMaestrosFinanzas = false;
        PermisoRegistrarGastos = false;
        PermisoRegistrarPagos = false;
        PermisoRegistrarIngresos = false;
        PermisoGestionarTareas = false;
        PermisoVerReportes = false;
    }

    [RelayCommand]
    private async Task GuardarAsync()
    {
        if (_padre?.UsuarioSeleccionado is null) return;

        var seleccionados = new List<string>();
        if (PermisoGestionarProductos) seleccionados.Add(Permisos.GestionarProductos);
        if (PermisoRecalcularStock) seleccionados.Add(Permisos.RecalcularStock);
        if (PermisoGestionarTablasMaestras) seleccionados.Add(Permisos.GestionarTablasMaestras);
        if (PermisoRegistrarMovimientos) seleccionados.Add(Permisos.RegistrarMovimientos);
        if (PermisoVerFinanzas) seleccionados.Add(Permisos.VerFinanzas);
        if (PermisoGestionarMaestrosFinanzas) seleccionados.Add(Permisos.GestionarMaestrosFinanzas);
        if (PermisoRegistrarGastos) seleccionados.Add(Permisos.RegistrarGastos);
        if (PermisoRegistrarPagos) seleccionados.Add(Permisos.RegistrarPagos);
        if (PermisoRegistrarIngresos) seleccionados.Add(Permisos.RegistrarIngresos);
        if (PermisoGestionarTareas) seleccionados.Add(Permisos.GestionarTareas);
        if (PermisoVerReportes) seleccionados.Add(Permisos.VerReportes);

        await _usuarios.GuardarPermisosAsync(_padre.UsuarioSeleccionado.Id, seleccionados);
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter FullyQualifiedName~PanelPermisosViewModelTests`
Expected: 9 tests PASS.

- [ ] **Step 5: Ampliar el constructor de `UsuariosAdminViewModel` para recibir y conectar el panel**

En `src/StockApp.Presentation/ViewModels/Administracion/UsuariosAdminViewModel.cs` (Task 12), reemplazar:

```csharp
    public UsuariosAdminViewModel(IUsuarioService usuarios, IConfirmacionService confirmacion)
    {
        _usuarios = usuarios;
        _confirmacion = confirmacion;
    }
```

por:

```csharp
    /// <summary>Panel de permisos de la columna derecha (Task 13). Recibido por DI —
    /// PanelPermisosViewModel se registra AddTransient sin depender de este tipo en su propio
    /// constructor (ver Decisión de diseño 2 de la Task 13: ViewLocator exige Views sin
    /// argumentos, así que la composición vive enteramente acá, no en el code-behind).</summary>
    public PanelPermisosViewModel PanelPermisos { get; }

    public UsuariosAdminViewModel(
        IUsuarioService usuarios, IConfirmacionService confirmacion, PanelPermisosViewModel panelPermisos)
    {
        _usuarios = usuarios;
        _confirmacion = confirmacion;
        PanelPermisos = panelPermisos;
        PanelPermisos.Conectar(this);
    }
```

Agregar `using StockApp.Presentation.ViewModels.Administracion;` no hace falta (mismo namespace — `PanelPermisosViewModel` vive en el mismo directorio).

Esto cambia la firma del constructor de 2 a 3 parámetros — actualizar el `Crear()` de `tests/StockApp.Presentation.Tests/ViewModels/Administracion/UsuariosAdminViewModelTests.cs` (Task 12):

```csharp
    private static (UsuariosAdminViewModel vm, Mock<IUsuarioService> svc, Mock<IConfirmacionService> confirm) Crear()
    {
        var svc = new Mock<IUsuarioService>();
        var confirm = new Mock<IConfirmacionService>();
        var vm = new UsuariosAdminViewModel(svc.Object, confirm.Object, new PanelPermisosViewModel(svc.Object));
        return (vm, svc, confirm);
    }
```

Agregar `using StockApp.Presentation.ViewModels.Administracion;` a ese archivo de test si el `Crear()` vive fuera de ese namespace (no debería hacer falta si el test ya está en `StockApp.Presentation.Tests.ViewModels.Administracion`).

- [ ] **Step 6: Insertar el panel en la View**

En `src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml`, reemplazar el comentario `<!-- Panel de permisos: ver PanelPermisosView, insertado acá en la Task 13 -->` por:

```xml
            <Border BorderBrush="Gray" BorderThickness="1" Padding="12" CornerRadius="4"
                    DataContext="{Binding PanelPermisos}">
                <StackPanel Spacing="8" IsEnabled="{Binding !EsAdminSeleccionado}">
                    <TextBlock Text="Acceso total" FontWeight="Bold" Foreground="Gray"
                               IsVisible="{Binding EsAdminSeleccionado}" />
                    <TextBlock Text="Catálogo" FontWeight="Bold" />
                    <CheckBox Content="Productos (ver, editar, recalcular stock)" IsChecked="{Binding Productos}" />
                    <CheckBox Content="Tablas maestras (categorías, proveedores, unidades)" IsChecked="{Binding PermisoGestionarTablasMaestras}" />
                    <CheckBox Content="Registrar movimientos de stock" IsChecked="{Binding PermisoRegistrarMovimientos}" />
                    <TextBlock Text="Finanzas" FontWeight="Bold" Margin="0,8,0,0" />
                    <CheckBox Content="Gastos y facturas" IsChecked="{Binding GastosYFacturas}" />
                    <CheckBox Content="Ingresos de caja" IsChecked="{Binding IngresosDeCaja}" />
                    <CheckBox Content="Ver Finanzas (libro caja, control POA, calendario)" IsChecked="{Binding PermisoVerFinanzas}" />
                    <CheckBox Content="Maestros de finanzas (fuentes, rubros, líneas POA)" IsChecked="{Binding PermisoGestionarMaestrosFinanzas}" />
                    <TextBlock Text="Tareas y reportes" FontWeight="Bold" Margin="0,8,0,0" />
                    <CheckBox Content="Tareas" IsChecked="{Binding PermisoGestionarTareas}" />
                    <CheckBox Content="Reportes" IsChecked="{Binding PermisoVerReportes}" />
                    <Button Content="Guardar permisos" Command="{Binding GuardarCommand}" Margin="0,12,0,0" />
                </StackPanel>
            </Border>
```

- [ ] **Step 7: Registrar `PanelPermisosViewModel` en DI**

En `src/StockApp.Presentation/App.axaml.cs`, junto al registro de `UsuariosAdminViewModel` (Task 12, Step 7):

```csharp
        services.AddTransient<StockApp.Presentation.ViewModels.Administracion.PanelPermisosViewModel>();
```

No hace falta ningún otro cambio de DI ni del code-behind de la View (`UsuariosAdminView.axaml.cs` queda exactamente como en la Task 12, Step 6): `PanelPermisosViewModel` se registra sin dependencias circulares (constructor de un solo parámetro, `IUsuarioService`), y `UsuariosAdminViewModel` (también `AddTransient`) lo recibe automáticamente como tercer parámetro — el contenedor de DI resuelve el grafo solo, sin necesidad de tocar `ViewLocator` ni pasarle nada a la View.

- [ ] **Step 8: Correr la suite completa de Presentation**

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: todo verde.

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Administracion/PanelPermisosViewModel.cs \
        src/StockApp.Presentation/ViewModels/Administracion/UsuariosAdminViewModel.cs \
        src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml \
        src/StockApp.Presentation/Views/Administracion/UsuariosAdminView.axaml.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Administracion/PanelPermisosViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Administracion/UsuariosAdminViewModelTests.cs
git commit -m "feat(permisos): panel de permisos con checkboxes agrupados y compuestos"
```

---

### Task 14: Gating del menú lateral por permiso configurable

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`
- Modify: `src/StockApp.Presentation/ViewModels/InicioViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml`
- Modify: `src/StockApp.Presentation/Views/InicioView.axaml`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs` (agregar)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelReportesTests.cs` (ajustar construcción — nuevo parámetro `IAuthService` del constructor, Step 11)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ShellMainFinanzasTests.cs` (ajustar construcción — ídem)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs` (agregar)

**Interfaces:**
- Consumes: `ICurrentSession.RolActual`/`.PermisosActuales` (Task 4); `IAuthService.ObtenerPermisosPropiosAsync()` → `Task<IReadOnlySet<string>>` (Task 9); `StockApp.Presentation.Services.RefrescoPermisos.DispararBestEffortAsync(Func<Task> operacion, string origen)` → `Task` (Task 9).
- Produces: `ShellMainViewModel.PuedeGestionarProductos`/`.PuedeRegistrarMovimientos`/`.PuedeGestionarTareas`/`.PuedeVerFinanzas`/`.PuedeGestionarMaestrosFinanzas`/`.PuedeGestionarTablasMaestras`/`.PuedeVerReportes` → `bool`; `InicioViewModel.PuedeVerReportes` → `bool`.

**Gap real hallado (excede la letra del spec, la cubre en espíritu):** el spec dice "los 16 `IsVisible={Binding EsAdmin}` se reemplazan uno a uno" — pero de los ítems de Finanzas/Movimientos/Productos/Tareas del sidebar, **ninguno tiene hoy ningún `IsVisible`** (siempre visibles, porque bajo el modelo viejo Admin y Operador tenían siempre los mismos permisos de esas secciones — `Permisos.cs`: "por ahora Admin Y Operador tienen todos"). Con permisos genuinamente revocables por operador, dejar esos ítems sin gating deja a un Operador sin `VerFinanzas` viendo igual el ítem "Gastos y facturas" en el menú (entra igual, rebota con 403 — no es un agujero de seguridad, pero sí una regresión de UX frente al propósito completo del feature). Esta task gatea también esos ítems, no solo los 16 originales — de lo contrario, las propiedades `Puede*` que el spec describe como "calculadas contra el cache local de permisos" (sección "Gating del menú lateral") quedarían sin usarse en la mitad de los casos reales.

**Cobertura completa de los 14 `IsVisible="{Binding EsAdmin}"` de `ShellMainView.axaml`:**

| Línea (antes de esta task) | Ítem | Permiso real | Acción |
|---|---|---|---|
| 219 | Header "Importación" | `ImportarPlanillas` (estructural) | Sin cambios |
| 226 | Botón Importar planillas | `ImportarPlanillas` (estructural) | Sin cambios |
| 240 | Header "Tablas maestras" | `GestionarTablasMaestras` | → `PuedeGestionarTablasMaestras` |
| 247 | Botón Categorías | `GestionarTablasMaestras` | → `PuedeGestionarTablasMaestras` |
| 259 | Botón Proveedores | `GestionarTablasMaestras` | → `PuedeGestionarTablasMaestras` |
| 271 | Botón Unidades de medida | `GestionarTablasMaestras` | → `PuedeGestionarTablasMaestras` |
| 285 | Header "Reportes" | `VerReportes` | → `PuedeVerReportes` |
| 292 | Botón Valorización | `VerReportes` | → `PuedeVerReportes` |
| 304 | Botón Stock por categoría | `VerReportes` | → `PuedeVerReportes` |
| 316 | Botón Historial por producto | `VerReportes` | → `PuedeVerReportes` |
| 328 | Botón Más movidos | `VerReportes` | → `PuedeVerReportes` |
| 340 | Botón Log de auditoría | `VerReportes` | → `PuedeVerReportes` |
| 354 | Header "Administración" | `GestionarDiagnostico`/`GestionarUsuarios` (estructurales) | Sin cambios |
| 361 | Botón Mantenimiento | `GestionarDiagnostico` (estructural) | Sin cambios |

Más los 2 de `InicioView.axaml` (líneas 237, 257 — botones de acceso rápido a Valorización/Auditoría) → `PuedeVerReportes`.

- [ ] **Step 1: Escribir los tests que fallan**

Agregar a `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs` (helper `Crear` ya existente en el archivo, Task 12 no lo modificó):

```csharp
    // ── Gating por permiso configurable (spec 2026-08-10) ─────────────────────

    [Theory]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarProductos), Permisos.GestionarProductos)]
    [InlineData(nameof(ShellMainViewModel.PuedeRegistrarMovimientos), Permisos.RegistrarMovimientos)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTareas), Permisos.GestionarTareas)]
    [InlineData(nameof(ShellMainViewModel.PuedeVerFinanzas), Permisos.VerFinanzas)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarMaestrosFinanzas), Permisos.GestionarMaestrosFinanzas)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTablasMaestras), Permisos.GestionarTablasMaestras)]
    [InlineData(nameof(ShellMainViewModel.PuedeVerReportes), Permisos.VerReportes)]
    public void Admin_TodasLasPropiedadesPuede_SonTrue(string propiedad, string permisoIgnorado)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>());

        var valor = (bool)typeof(ShellMainViewModel).GetProperty(propiedad)!.GetValue(vm)!;

        Assert.True(valor);
    }

    [Theory]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarProductos), Permisos.GestionarProductos)]
    [InlineData(nameof(ShellMainViewModel.PuedeRegistrarMovimientos), Permisos.RegistrarMovimientos)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTareas), Permisos.GestionarTareas)]
    [InlineData(nameof(ShellMainViewModel.PuedeVerFinanzas), Permisos.VerFinanzas)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarMaestrosFinanzas), Permisos.GestionarMaestrosFinanzas)]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTablasMaestras), Permisos.GestionarTablasMaestras)]
    [InlineData(nameof(ShellMainViewModel.PuedeVerReportes), Permisos.VerReportes)]
    public void Operador_ConElPermisoEnPermisosActuales_LaPropiedadEsTrue(string propiedad, string permiso)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { permiso });
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>());

        var valor = (bool)typeof(ShellMainViewModel).GetProperty(propiedad)!.GetValue(vm)!;

        Assert.True(valor);
    }

    [Theory]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarProductos))]
    [InlineData(nameof(ShellMainViewModel.PuedeRegistrarMovimientos))]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTareas))]
    [InlineData(nameof(ShellMainViewModel.PuedeVerFinanzas))]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarMaestrosFinanzas))]
    [InlineData(nameof(ShellMainViewModel.PuedeGestionarTablasMaestras))]
    [InlineData(nameof(ShellMainViewModel.PuedeVerReportes))]
    public void Operador_SinNingunPermisoEnPermisosActuales_LaPropiedadEsFalse(string propiedad)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>());

        var valor = (bool)typeof(ShellMainViewModel).GetProperty(propiedad)!.GetValue(vm)!;

        Assert.False(valor);
    }
```

Agregar `using StockApp.Application.Authorization;` al tope del archivo (para `Permisos`).

Agregar a `tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs`. El helper real del archivo es `Crear(UsuarioSesion usuario, CalendarioPagosDto? calendario = null, SaludBackupDto? salud = null, IReadOnlyList<Tarea>? tareas = null, Exception? errorTareas = null)`, devuelve la tupla `(InicioViewModel vm, Mock<ICurrentSession> sessionMock, Mock<INavigationService> navMock, Mock<IFinanzasVistasService> finanzasMock, Mock<IBackupsService> backupsMock)` y el rol se fija por `usuario.Rol` (no hay un `sessionMock` propio a inyectar aparte del que arma el helper) — para variar `PermisosActuales` sin tocar la firma del helper, se lo configura DESPUÉS de `Crear`:

```csharp
    [Fact]
    public void PuedeVerReportes_Admin_EsTrue()
    {
        var (vm, sessionMock, _, _, _) = Crear(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());

        Assert.True(vm.PuedeVerReportes);
    }

    [Fact]
    public void PuedeVerReportes_OperadorSinPermiso_EsFalse()
    {
        var (vm, sessionMock, _, _, _) = Crear(new UsuarioSesion(2, "operador", RolUsuario.Operador, null));
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());

        Assert.False(vm.PuedeVerReportes);
    }

    [Fact]
    public void PuedeVerReportes_OperadorConPermiso_EsTrue()
    {
        var (vm, sessionMock, _, _, _) = Crear(new UsuarioSesion(2, "operador", RolUsuario.Operador, null));
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string> { Permisos.VerReportes });

        Assert.True(vm.PuedeVerReportes);
    }
```

Agregar `using StockApp.Application.Authorization;` al tope de ese archivo si falta (para `Permisos.VerReportes`).

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ShellMainViewModelTests|FullyQualifiedName~InicioViewModelTests"`
Expected: FALLA de compilación — las 7 propiedades `Puede*` no existen en `ShellMainViewModel`, `PuedeVerReportes` no existe en `InicioViewModel`.

- [ ] **Step 3: Agregar las propiedades a `ShellMainViewModel`**

En `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`, agregar después de `public bool EsAdmin => ...`:

```csharp
    // ── Gating por permiso configurable (spec 2026-08-10) ─────────────────────
    // Misma condición que evalúa AuthorizationService.Verificar del lado servidor: Admin
    // siempre pasa, Operador según PermisosActuales. Esto es cosmética, no seguridad — la
    // autorización real vive en las dos barreras de la API (HTTP + Application); si el binding
    // tuviera un bug y mostrara un ítem de más, el peor caso es un clic que rebota con 403.

    public bool PuedeGestionarProductos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarProductos);

    public bool PuedeRegistrarMovimientos =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.RegistrarMovimientos);

    public bool PuedeGestionarTareas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarTareas);

    public bool PuedeVerFinanzas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerFinanzas);

    public bool PuedeGestionarMaestrosFinanzas =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarMaestrosFinanzas);

    public bool PuedeGestionarTablasMaestras =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.GestionarTablasMaestras);

    public bool PuedeVerReportes =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerReportes);
```

Agregar `using StockApp.Application.Authorization;` al tope del archivo.

- [ ] **Step 4: Agregar la propiedad a `InicioViewModel`**

En `src/StockApp.Presentation/ViewModels/InicioViewModel.cs`, agregar después de `public bool EsAdmin => ...`:

```csharp
    public bool PuedeVerReportes =>
        _session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(Permisos.VerReportes);
```

Agregar `using StockApp.Application.Authorization;` al tope del archivo.

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ShellMainViewModelTests|FullyQualifiedName~InicioViewModelTests"`
Expected: todos PASS.

- [ ] **Step 6: Reemplazar los 4 `EsAdmin` de "Tablas maestras" por `PuedeGestionarTablasMaestras`**

Run (reemplazo anclado por número de línea — solo la palabra `EsAdmin`, sin tocar el resto de la línea; los números son los del archivo ANTES de esta task, ningún cambio previo tocó este archivo):

```bash
sed -i '240s/EsAdmin/PuedeGestionarTablasMaestras/' src/StockApp.Presentation/Views/ShellMainView.axaml
sed -i '247s/EsAdmin/PuedeGestionarTablasMaestras/' src/StockApp.Presentation/Views/ShellMainView.axaml
sed -i '259s/EsAdmin/PuedeGestionarTablasMaestras/' src/StockApp.Presentation/Views/ShellMainView.axaml
sed -i '271s/EsAdmin/PuedeGestionarTablasMaestras/' src/StockApp.Presentation/Views/ShellMainView.axaml
```

- [ ] **Step 7: Reemplazar los 6 `EsAdmin` de "Reportes" por `PuedeVerReportes`**

```bash
sed -i '285s/EsAdmin/PuedeVerReportes/' src/StockApp.Presentation/Views/ShellMainView.axaml
sed -i '292s/EsAdmin/PuedeVerReportes/' src/StockApp.Presentation/Views/ShellMainView.axaml
sed -i '304s/EsAdmin/PuedeVerReportes/' src/StockApp.Presentation/Views/ShellMainView.axaml
sed -i '316s/EsAdmin/PuedeVerReportes/' src/StockApp.Presentation/Views/ShellMainView.axaml
sed -i '328s/EsAdmin/PuedeVerReportes/' src/StockApp.Presentation/Views/ShellMainView.axaml
sed -i '340s/EsAdmin/PuedeVerReportes/' src/StockApp.Presentation/Views/ShellMainView.axaml
```

Verificar con `git diff src/StockApp.Presentation/Views/ShellMainView.axaml` que las líneas 219, 226, 354, 361 (Importación/Administración, estructurales) siguen intactas con `EsAdmin` — no deben tocarse.

- [ ] **Step 8: Agregar gating a Productos, Movimientos y Tareas (hoy sin ningún `IsVisible`)**

En `src/StockApp.Presentation/Views/ShellMainView.axaml`, reemplazar el bloque de los botones de Productos/Movimientos/Tareas (líneas ~61-137 antes de esta task):

```xml
                <!-- Productos: visible para Admin y Operador -->
                <Button Command="{Binding NavProductosCommand}"
                        Classes="ghost"
                        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Productos}"
                        HorizontalAlignment="Stretch">
```

por:

```xml
                <!-- Productos: gateado por GestionarProductos (spec 2026-08-10) -->
                <Button Command="{Binding NavProductosCommand}"
                        Classes="ghost"
                        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Productos}"
                        HorizontalAlignment="Stretch"
                        IsVisible="{Binding PuedeGestionarProductos}">
```

Y para cada uno de los 4 botones de movimientos (`NavRegistrarEntradaCommand`, `NavIngresoPorFacturaCommand`, `NavRegistrarSalidaCommand`, `NavHistorialMovimientosCommand`), agregar `IsVisible="{Binding PuedeRegistrarMovimientos}"` al tag `<Button>` (mismo patrón: agregar el atributo antes del `>` de cierre del `<Button ... HorizontalAlignment="Stretch">`).

Para la sección Tareas, reemplazar:

```xml
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
```

por:

```xml
                <!-- Tareas: gateado por GestionarTareas (spec 2026-08-10) -->
                <TextBlock Text="Tareas"
                           Classes="caption"
                           Foreground="{DynamicResource SidebarTextoBrush}"
                           FontWeight="SemiBold"
                           Margin="8,8,0,4"
                           IsVisible="{Binding PuedeGestionarTareas}"
                           Opacity="0.6" />

                <Button Command="{Binding NavTareasCommand}"
                        Classes="ghost"
                        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Tareas}"
                        HorizontalAlignment="Stretch"
                        IsVisible="{Binding PuedeGestionarTareas}">
```

- [ ] **Step 9: Agregar gating a la sección Finanzas (hoy sin ningún `IsVisible`)**

Reemplazar el header:

```xml
                <!-- Finanzas: visible para Admin y Operador (spec Finanzas §9) -->
                <TextBlock Text="Finanzas"
                           Classes="caption"
                           Foreground="{DynamicResource SidebarTextoBrush}"
                           FontWeight="SemiBold"
                           Margin="8,8,0,4"
                           Opacity="0.6" />
```

por:

```xml
                <!-- Finanzas: gateado por VerFinanzas (spec 2026-08-10). Caso borde aceptado
                     (cosmética, no seguridad): un Operador con SOLO GestionarMaestrosFinanzas
                     (sin VerFinanzas) ve el botón "Maestros de finanzas" sin este header encima
                     — no hay overlap real en la práctica (VerFinanzas es el permiso base de
                     Finanzas que casi siempre se concede junto con los demás). -->
                <TextBlock Text="Finanzas"
                           Classes="caption"
                           Foreground="{DynamicResource SidebarTextoBrush}"
                           FontWeight="SemiBold"
                           Margin="8,8,0,4"
                           IsVisible="{Binding PuedeVerFinanzas}"
                           Opacity="0.6" />
```

Y agregar `IsVisible="{Binding PuedeVerFinanzas}"` a los botones `NavGastosCommand`, `NavIngresosCommand`, `NavLibroCajaCommand`, `NavControlPoaCommand`, `NavCalendarioPagosCommand` (mismo patrón que el Step 8: agregar el atributo al `<Button>`), y `IsVisible="{Binding PuedeGestionarMaestrosFinanzas}"` al botón `NavMaestrosFinanzasCommand` (permiso distinto — gestionar maestros de finanzas no es lo mismo que verlas).

- [ ] **Step 10: Agregar gating a los 2 accesos rápidos de `InicioView.axaml`**

```bash
sed -i '237s/EsAdmin/PuedeVerReportes/' src/StockApp.Presentation/Views/InicioView.axaml
sed -i '257s/EsAdmin/PuedeVerReportes/' src/StockApp.Presentation/Views/InicioView.axaml
```

- [ ] **Step 11: Refresco de permisos al navegar entre secciones (spec decisión 7, segunda mitad)**

En `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`, en `OnNavegacionCambiada` (el handler ya suscripto a `INavigationService.Cambiado` desde el constructor), agregar un refresco en segundo plano — fire-and-forget, nunca bloquea la navegación ni la revienta si falla. Usa el mismo helper compartido `RefrescoPermisos` (Task 9) que consumen `LoginViewModel` (Task 9), `PanelPermisosViewModel` (Task 13) y `App.axaml.cs` (Task 15) — pre-flight, corrección C: un solo lugar con el patrón try/catch de mejor esfuerzo, no cuatro copias:

```csharp
    private void OnNavegacionCambiada()
    {
        if (!ReferenceEquals(_navigation.Actual, this))
            CurrentContent = _navigation.Actual;

        // Refresco de permisos al navegar (spec decisión 7): best-effort, en segundo plano —
        // si el Admin revocó un permiso mientras la sesión seguía abierta, el menú se actualiza
        // sin esperar a la próxima acción que dispare un 403 (Task 15). No bloquea la
        // navegación: si la API está caída, el usuario sigue navegando con el cache viejo.
        _ = RefrescoPermisos.DispararBestEffortAsync(
            () => _authService.ObtenerPermisosPropiosAsync(), nameof(ShellMainViewModel));
    }
```

`StockApp.Presentation.Services` (namespace de `RefrescoPermisos`) ya está importado en este archivo — no hace falta agregar ningún `using` para esto.

Esto requiere agregar `IAuthService _authService` al constructor de `ShellMainViewModel` (hoy no lo tiene). Reemplazar:

```csharp
    private readonly ICurrentSession    _session;
    private readonly INavigationService _navigation;
    private readonly IInfoApp           _infoApp;
    private readonly IConfirmacionService _confirmacion;
```

```csharp
    public ShellMainViewModel(
        ICurrentSession session,
        INavigationService navigation,
        IInfoApp infoApp,
        IConfirmacionService confirmacion)
    {
        _session      = session;
        _navigation   = navigation;
        _infoApp      = infoApp;
        _confirmacion = confirmacion;
```

por:

```csharp
    private readonly ICurrentSession    _session;
    private readonly INavigationService _navigation;
    private readonly IInfoApp           _infoApp;
    private readonly IConfirmacionService _confirmacion;
    private readonly IAuthService       _authService;
```

```csharp
    public ShellMainViewModel(
        ICurrentSession session,
        INavigationService navigation,
        IInfoApp infoApp,
        IConfirmacionService confirmacion,
        IAuthService authService)
    {
        _session      = session;
        _navigation   = navigation;
        _infoApp      = infoApp;
        _confirmacion = confirmacion;
        _authService  = authService;
```

Agregar `using StockApp.Application.Auth;` al tope del archivo.

Esto cambia la firma del constructor de 4 a 5 parámetros — actualizar TODAS las construcciones de `ShellMainViewModel` en `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs`, `ShellMainViewModelReportesTests.cs` y `ViewModels/Finanzas/ShellMainFinanzasTests.cs` (los 3 archivos de test que lo construyen — ver `grep -rn "new ShellMainViewModel(" tests/`) agregando `Mock.Of<IAuthService>()` como quinto argumento en cada una.

Run: `grep -rn "new ShellMainViewModel(" tests/StockApp.Presentation.Tests/`
Para cada resultado, agregar el quinto argumento.

- [ ] **Step 12: Correr la suite completa de Presentation**

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: todo verde.

- [ ] **Step 13: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs \
        src/StockApp.Presentation/ViewModels/InicioViewModel.cs \
        src/StockApp.Presentation/Views/ShellMainView.axaml \
        src/StockApp.Presentation/Views/InicioView.axaml \
        tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelReportesTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ShellMainFinanzasTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs
git commit -m "feat(permisos): gating del menu lateral por permiso configurable"
```

---

### Task 15: Manejo central del 403 en `AuthTokenHandler`

**Files:**
- Modify: `src/StockApp.ApiClient/ApiSession.cs`
- Modify: `src/StockApp.ApiClient/AuthTokenHandler.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.ApiClient.Tests/AuthTokenHandlerTests.cs` (agregar)
- Test: `tests/StockApp.ApiClient.Tests/ApiSessionTests.cs` (agregar)

**Interfaces:**
- Consumes: `IAuthService.ObtenerPermisosPropiosAsync()` → `Task<IReadOnlySet<string>>` (Task 9), `IConfirmacionService.InformarAsync` (existente), `StockApp.Presentation.Services.RefrescoPermisos.DispararBestEffortAsync(Func<Task> operacion, string origen)` → `Task` (Task 9).
- Produces: `ApiSession.AccesoRevocado` → `event Action?`.

- [ ] **Step 1: Escribir los tests que fallan**

Agregar a `tests/StockApp.ApiClient.Tests/AuthTokenHandlerTests.cs` (mismo estilo que `Un401ConToken_CierraSesionYDisparaElEvento`, ya presente en el archivo):

```csharp
    [Fact]
    public async Task Un403_DisparaAccesoRevocado()
    {
        var session = SesionConToken();
        var disparado = false;
        session.AccesoRevocado += () => disparado = true;
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var http = TestHttp.CrearCliente(fake, session);

        var response = await http.GetAsync("finanzas/gastos");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(disparado);
        // A diferencia del 401, la sesión SIGUE siendo válida — un 403 significa que el
        // Admin cambió algo, no que el token venció.
        Assert.True(session.EstaAutenticado);
    }

    [Fact]
    public async Task Un200_NoDisparaAccesoRevocado()
    {
        var session = SesionConToken();
        var disparado = false;
        session.AccesoRevocado += () => disparado = true;
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = TestHttp.CrearCliente(fake, session);

        await http.GetAsync("categorias");

        Assert.False(disparado);
    }
```

Agregar a `tests/StockApp.ApiClient.Tests/ApiSessionTests.cs`:

```csharp
    [Fact]
    public void DispararAccesoRevocado_InvocaElEvento()
    {
        // DispararAccesoRevocado es internal — visible acá porque StockApp.ApiClient.csproj
        // declara InternalsVisibleTo hacia StockApp.ApiClient.Tests (mismo mecanismo que ya
        // usan los tests de SesionVencida/LicenciaDesactivada en AuthTokenHandlerTests.cs).
        var session = new ApiSession();
        var disparado = false;
        session.AccesoRevocado += () => disparado = true;

        session.DispararAccesoRevocado();

        Assert.True(disparado);
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~AuthTokenHandlerTests|FullyQualifiedName~ApiSessionTests"`
Expected: FALLA de compilación — `AccesoRevocado`/`DispararAccesoRevocado` no existen en `ApiSession`.

- [ ] **Step 3: Agregar el evento a `ApiSession`**

En `src/StockApp.ApiClient/ApiSession.cs`, agregar junto a `SesionVencida`/`LicenciaDesactivada`:

```csharp
    /// <summary>
    /// El servidor respondió 403 Forbidden: el Admin cambió los permisos del usuario mientras
    /// la sesión seguía abierta (spec 2026-08-10). A diferencia de SesionVencida, la sesión
    /// SIGUE siendo válida — no se cierra. La composition root lo cablea a un mensaje claro y
    /// a un refresco de GET /auth/permisos (App.axaml.cs).
    /// </summary>
    public event Action? AccesoRevocado;
```

Y junto a `DispararSesionVencida`/`DispararLicenciaDesactivada`:

```csharp
    /// <summary>Lo invoca AuthTokenHandler ante un 403 (internal + InternalsVisibleTo).</summary>
    internal void DispararAccesoRevocado() => AccesoRevocado?.Invoke();
```

- [ ] **Step 4: Disparar el evento en `AuthTokenHandler`**

En `src/StockApp.ApiClient/AuthTokenHandler.cs`, en `SendAsync`, agregar junto a la rama de 423:

```csharp
        if (response.StatusCode == (HttpStatusCode)423)
        {
            _session.DispararLicenciaDesactivada();
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            _session.DispararAccesoRevocado();
        }

        return response;
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~AuthTokenHandlerTests|FullyQualifiedName~ApiSessionTests"`
Expected: todos PASS.

- [ ] **Step 6: Cablear el evento en la composition root**

En `src/StockApp.Presentation/App.axaml.cs`, en `OnFrameworkInitializationCompleted`, junto al cableado de `SesionVencida`/`LicenciaDesactivada` (líneas ~92-101). El refresco de permisos usa el mismo helper compartido `RefrescoPermisos` (Task 9) que consumen `LoginViewModel` (Task 9), `PanelPermisosViewModel` (Task 13) y `ShellMainViewModel` (Task 14) — pre-flight, corrección C: un solo lugar con el patrón try/catch de mejor esfuerzo. El mensaje al usuario (`InformarAsync`) NO pasa por el helper — no es la parte "mejor esfuerzo" de este flujo, es el aviso que sí tiene que verse:

```csharp
            apiSession.LicenciaDesactivada += () => uiDispatcher.Post(
                () => shell.MostrarBloqueoLicencia());

            // Acceso revocado (spec 2026-08-10): un 403 con sesión válida significa que el
            // Admin cambió los permisos de este usuario mientras la sesión seguía abierta —
            // a diferencia de SesionVencida, NO se cierra sesión ni se navega. Se avisa y se
            // refresca el cache de permisos para que el menú deje de mostrar ítems ya
            // revocados. Best-effort: si el refresco falla, el aviso igual se mostró.
            apiSession.AccesoRevocado += () => uiDispatcher.Post(async () =>
            {
                var confirmacion = _serviceProvider!.GetRequiredService<IConfirmacionService>();
                await confirmacion.InformarAsync("Ya no tenés acceso a esta sección.");

                var authService = _serviceProvider!.GetRequiredService<IAuthService>();
                await RefrescoPermisos.DispararBestEffortAsync(
                    () => authService.ObtenerPermisosPropiosAsync(), "AccesoRevocado");
            });
```

Agregar `using StockApp.Presentation.Services;` si falta (ya está, por `IConfirmacionService` usado en otros lugares del archivo — el mismo `using` cubre `RefrescoPermisos`, mismo namespace).

- [ ] **Step 7: Correr la suite completa de ApiClient y Presentation**

Run: `dotnet test tests/StockApp.ApiClient.Tests && dotnet test tests/StockApp.Presentation.Tests`
Expected: todo verde.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.ApiClient/ApiSession.cs \
        src/StockApp.ApiClient/AuthTokenHandler.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.ApiClient.Tests/AuthTokenHandlerTests.cs \
        tests/StockApp.ApiClient.Tests/ApiSessionTests.cs
git commit -m "feat(permisos): manejo central del 403 con aviso y refresco de permisos"
```

---

### Task 16: Suite completa y verificación orgánica

**Files:** ninguno (task de verificación, no de código).

- [ ] **Step 1: Correr la suite completa de los 6 proyectos de test**

Run (uno por uno, para que un fallo en un proyecto no oculte el resultado de los demás — **no** usar un conteo hardcodeado de tests esperados: correr y leer el resultado real, el número final de este plan no es una promesa verificable de antemano):

```bash
dotnet test tests/StockApp.Domain.Tests
dotnet test tests/StockApp.Application.Tests
dotnet test tests/StockApp.Infrastructure.Tests
dotnet test tests/StockApp.Api.Tests
dotnet test tests/StockApp.ApiClient.Tests
dotnet test tests/StockApp.Presentation.Tests
```

Expected: **todo verde** en los 6 proyectos. Si estás en un worktree, los ~16 rojos de fixtures `.ods` en `Infrastructure.Tests` son falsos negativos preexistentes (fixtures gitignored no copiadas por `git worktree`) — no son de este plan.

- [ ] **Step 2: Grep final de que no queda ningún rastro del modelo viejo**

```bash
grep -rn "AccionesOperador" src/ && echo "FALTA LIMPIAR" || echo "OK: sin rastros"
grep -rn "\.TienePermiso(" src/ && echo "FALTA LIMPIAR" || echo "OK: sin rastros"
grep -rn "Verificar(RolUsuario" src/ && echo "FALTA LIMPIAR" || echo "OK: sin rastros"
```

Expected: los 3 greps devuelven "OK: sin rastros" — `AccionesOperador` no debería existir en ningún lado (reemplazada por `PermisosInicialesOperador` desde la Task 5, eliminada del todo en la Task 7); `TienePermiso` y `Verificar(RolUsuario` no deberían tener ningún consumidor de producción tras la Task 7.

- [ ] **Step 3: Verificación orgánica — dos sesiones simultáneas (spec, sección Testing)**

Con la API y el desktop corriendo contra Postgres real:

1. Abrir **dos** instancias del desktop (o una instancia + `curl`/Postman para la segunda "sesión").
2. Sesión 1: loguearse como Admin.
3. Sesión 2: loguearse como un usuario Operador que tenga `VerFinanzas` concedido (crear uno nuevo desde la pantalla de Administración de usuarios de la Sesión 1 si no existe, o usar uno con permisos ya sembrados por el backfill de la Task 1).
4. En la Sesión 2 (Operador), navegar a "Gastos y facturas" — debe entrar sin problema (tiene `VerFinanzas`).
5. Sin cerrar la Sesión 2, volver a la Sesión 1 (Admin) → Administración de usuarios → seleccionar al mismo Operador → destildar "Gastos y facturas" (o directamente el checkbox "Ver Finanzas") → Guardar.
6. En la Sesión 2, sin recargar ni volver a loguearse, hacer clic en cualquier pantalla de Finanzas (o repetir la navegación a "Gastos y facturas").
7. **Resultado esperado**: la Sesión 2 rebota con un mensaje de error claro ("Ya no tenés acceso a esta sección.", disparado por `AuthTokenHandler`/`ApiSession.AccesoRevocado`, Task 15) — **sin** haber cerrado sesión ni vuelto a loguearse. Esto confirma de punta a punta: cache de `IProveedorPermisos` invalidada al guardar (Task 3) → `PermisoAuthorizationHandler` rechaza el próximo request (Task 7) → `AuthTokenHandler` detecta el 403 y avisa (Task 15).
8. Verificar también que el ítem "Gastos y facturas" del menú lateral de la Sesión 2 desaparece tras el próximo refresco de navegación (Task 14, Step 11 — el refresco fire-and-forget en `OnNavegacionCambiada`), sin necesidad de reiniciar la app.
9. Verificar el caso inverso: en la Sesión 1, volver a tildar el permiso y Guardar; en la Sesión 2, navegar — debe volver a entrar sin re-loguearse.
10. Verificar el panel de un usuario Admin en la pantalla de Administración de usuarios: debe mostrarse deshabilitado con la leyenda "Acceso total", sin checkboxes tildables.
11. Verificar el checkbox compuesto: tildar "Gastos y facturas" en el panel de un Operador y confirmar que el checkbox "Ver Finanzas" se marca solo, en el acto, sin guardar todavía.

- [ ] **Step 4: Registrar el resultado**

Si algún paso del Step 3 falla, NO marcar esta task como completa — volver a la task correspondiente (probablemente Task 3, 7, 8 o 15, según en qué eslabón se cortó la cadena) y corregir antes de continuar. Si todo pasa, la implementación de "Permisos por operador" queda funcionalmente completa según el spec `docs/superpowers/specs/2026-08-10-permisos-por-operador-design.md`.

No hay commit en esta task (es de verificación); si el Step 3 revela un bug, el fix correspondiente se commitea contra la task de origen del bug, con su propio mensaje descriptivo — no acá.
