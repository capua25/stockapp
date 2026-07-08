# Fase 1 — PostgreSQL + transacción atómica de stock — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar la capa de datos de StockApp de SQLite a PostgreSQL con un único proveedor (Npgsql) y convertir el registro de movimientos en una transacción atómica con UPDATE condicional, probada contra Postgres real con tests de concurrencia.

**Architecture:** Clean Architecture (Domain / Application / Infrastructure / Presentation). Infrastructure deja de referenciar SQLite y pasa a Npgsql con un solo set de migraciones. El registro de un movimiento se envuelve en una transacción explícita que ejecuta un UPDATE condicional atómico sobre `Producto.StockActual` (la base serializa la fila y hace cumplir "no negativo"), más el insert del movimiento y del log de auditoría; el repositorio devuelve un resultado tipado que el service traduce a excepción de dominio.

**Tech Stack:** .NET 10, C#, EF Core 10 + Npgsql, xUnit 2.5.3, Moq, Testcontainers.PostgreSql

## Global Constraints

- Target framework `net10.0` en todos los proyectos.
- Conventional commits, SIN línea `Co-Authored-By` ni atribución a IA.
- TDD por capas: test que falla → verificar que falla → implementación mínima → verificar verde → commit.
- Switch TOTAL a PostgreSQL: NO debe quedar ninguna referencia a SQLite (paquetes `Microsoft.EntityFrameworkCore.Sqlite`, `SQLitePCLRaw.*`, `Microsoft.Data.Sqlite`, ni llamadas `UseSqlite`) en el código final de producción ni de tests.
- Un solo set de migraciones: se descartan las 3 migraciones SQLite y se genera una única `InitialCreate` para Postgres.
- Transacción atómica del stock con UPDATE condicional (design §5): `UPDATE Producto SET StockActual = StockActual - @cant WHERE Id = @id AND StockActual >= @cant`; 0 filas afectadas ⇒ stock insuficiente.
- Los tests de concurrencia corren contra Postgres real (Testcontainers), nunca SQLite. Requieren Docker disponible.
- El flag `forzar=true` en salidas permite stock negativo (bypass del guard condicional).
- Sin auto-retry en writes.
- Npgsql exige `DateTime` con `Kind=Utc` para columnas timestamp; el código ya usa `DateTime.UtcNow` en todos los writes (verificado). No introducir `DateTime.Now`.

---

### Task 1: Swap de proveedor SQLite → Npgsql

Quita los paquetes SQLite de Infrastructure y Presentation, agrega Npgsql, apunta la factory de design-time y el arranque de la app a `UseNpgsql` con connection string de configuración, y ajusta la sintaxis del índice filtrado en `OnModelCreating`.

**Files:**
- Modify: `src/StockApp.Infrastructure/StockApp.Infrastructure.csproj:7-20`
- Modify: `src/StockApp.Presentation/StockApp.Presentation.csproj:49-58`
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs:36-37`
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContextFactory.cs:13-20`
- Modify: `src/StockApp.Presentation/App.axaml.cs:136-141`
- Modify: `src/StockApp.Presentation/appsettings.json:1-6`

**Interfaces:**
- Produces: connection string `ConnectionStrings:Default` en `appsettings.json`, consumida por `App.axaml.cs`. La factory de design-time usa un string hardcodeado independiente.

- [ ] **Step 1: Reemplazar paquetes SQLite por Npgsql en Infrastructure.csproj**

Reemplazar el bloque `ItemGroup` de paquetes (líneas 7-20) por:

```xml
  <ItemGroup>
    <PackageReference Include="BCrypt.Net-Next" Version="4.2.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.*" />
    <PackageReference Include="Velopack" Version="1.2.0" />
  </ItemGroup>
```

> DECISIÓN A CONFIRMAR: se usa `Version="10.*"` (misma convención que los paquetes EF existentes). Si `10.*` no resuelve una versión publicada, correr `dotnet add src/StockApp.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL` para fijar la última compatible con EF Core 10 y actualizar el número aquí. Al quitar `SQLitePCLRaw.*` desaparece también la deuda de seguridad NU1903 documentada en el comentario original.

- [ ] **Step 2: Reemplazar paquetes SQLite por Npgsql en Presentation.csproj**

En `src/StockApp.Presentation/StockApp.Presentation.csproj`, borrar las líneas 53-58 (el `PackageReference` de `Microsoft.EntityFrameworkCore.Sqlite`, el comentario del CVE y los dos `SQLitePCLRaw.*`) y en su lugar dejar una sola línea:

```xml
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.*" />
```

Conservar intacto `Microsoft.EntityFrameworkCore.Design` (líneas 49-52).

- [ ] **Step 3: Ajustar el índice filtrado de CodigoBarras a sintaxis Npgsql**

En `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`, reemplazar líneas 36-37:

```csharp
            e.HasIndex(p => p.CodigoBarras).IsUnique()
                .HasFilter("[CodigoBarras] IS NOT NULL");
```

por:

```csharp
            e.HasIndex(p => p.CodigoBarras).IsUnique()
                .HasFilter("\"CodigoBarras\" IS NOT NULL");
```

- [ ] **Step 4: Apuntar la factory de design-time a Npgsql**

En `src/StockApp.Infrastructure/Persistence/AppDbContextFactory.cs`, reemplazar el cuerpo de `CreateDbContext` (líneas 15-19):

```csharp
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=stockapp_design;Username=stockapp;Password=stockapp")
            .Options;

        return new AppDbContext(options);
```

> El string es solo para design-time (`dotnet ef migrations add` construye el modelo sin conectarse; no requiere un Postgres corriendo).

- [ ] **Step 5: Agregar la connection string a appsettings.json**

Reemplazar el contenido completo de `src/StockApp.Presentation/appsettings.json` por:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=stockapp;Username=stockapp;Password=stockapp"
  },
  "Updater": {
    "GitHubRepoUrl": "https://github.com/capua25/stockapp",
    "GitHubPrerelease": false
  }
}
```

> DOCUMENTACIÓN OPERATIVA: correr la app o los tests de Infrastructure ahora requiere un PostgreSQL accesible. Para desarrollo local levantar un contenedor:
> `docker run --name stockapp-pg -e POSTGRES_USER=stockapp -e POSTGRES_PASSWORD=stockapp -e POSTGRES_DB=stockapp -p 5432:5432 -d postgres:16-alpine`

- [ ] **Step 6: Cambiar el registro del DbContext en App.axaml.cs a UseNpgsql**

En `src/StockApp.Presentation/App.axaml.cs`, reemplazar el registro del DbContext (líneas 136-141):

```csharp
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "Falta la cadena de conexión 'ConnectionStrings:Default' en appsettings.json. " +
                    "Se requiere un PostgreSQL accesible (contenedor Docker local u on-premise).");
            options.UseNpgsql(connectionString);
        }, ServiceLifetime.Transient);
```

> Se mantiene `ServiceLifetime.Transient` en esta fase (el cambio a Scoped es Fase 2). `configuration.GetConnectionString` ya está disponible vía el `using Microsoft.Extensions.Configuration;` presente en el archivo (línea 11).

- [ ] **Step 7: Compilar Infrastructure y Presentation**

Run: `dotnet build src/StockApp.Infrastructure src/StockApp.Presentation`
Expected: build correcto de ambos proyectos (los tests de Infrastructure quedarán rojos hasta la Task 3 — es esperado y se documenta abajo).

> NOTA DE ACOPLAMIENTO (igual que en Fase 0): las Tareas 1, 2 y 3 son un clúster. Al quitar SQLite, `StockApp.Infrastructure.Tests` NO compila (usa `Microsoft.Data.Sqlite` / `UseSqlite`) hasta que Testcontainers reemplace el setup en la Task 3. Es correcto avanzar con el build de solución roto en el proyecto de tests entre la Task 1 y la Task 3.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Infrastructure/StockApp.Infrastructure.csproj \
        src/StockApp.Presentation/StockApp.Presentation.csproj \
        src/StockApp.Infrastructure/Persistence/AppDbContext.cs \
        src/StockApp.Infrastructure/Persistence/AppDbContextFactory.cs \
        src/StockApp.Presentation/App.axaml.cs \
        src/StockApp.Presentation/appsettings.json
git commit -m "feat(datos): reemplazar proveedor SQLite por Npgsql/PostgreSQL"
```

---

### Task 2: Descartar migraciones SQLite y generar InitialCreate para Postgres

Elimina las 3 migraciones SQLite y el snapshot, y genera una única migración `InitialCreate` con el proveedor Npgsql.

**Files:**
- Delete: `src/StockApp.Infrastructure/Migrations/20260608223621_InitialCreate.cs`
- Delete: `src/StockApp.Infrastructure/Migrations/20260608223621_InitialCreate.Designer.cs`
- Delete: `src/StockApp.Infrastructure/Migrations/20260609211956_AddCatalogoExtensions.cs`
- Delete: `src/StockApp.Infrastructure/Migrations/20260609211956_AddCatalogoExtensions.Designer.cs`
- Delete: `src/StockApp.Infrastructure/Migrations/20260610221527_AddMovimientoStockIndices.cs`
- Delete: `src/StockApp.Infrastructure/Migrations/20260610221527_AddMovimientoStockIndices.Designer.cs`
- Delete: `src/StockApp.Infrastructure/Migrations/AppDbContextModelSnapshot.cs`
- Create: `src/StockApp.Infrastructure/Migrations/<timestamp>_InitialCreate.cs` (generado por EF)
- Create: `src/StockApp.Infrastructure/Migrations/<timestamp>_InitialCreate.Designer.cs` (generado por EF)
- Create: `src/StockApp.Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (generado por EF)

**Interfaces:**
- Produces: el snapshot y la migración `InitialCreate` de Postgres; los nombres de tabla generados (esperados: `Usuarios`, `Productos`, `Categorias`, `Proveedores`, `UnidadesMedida`, `MovimientosStock`, `LogsAuditoria`) los consume el `TRUNCATE` de la Task 3.

- [ ] **Step 1: Borrar las migraciones SQLite y el snapshot**

```bash
git rm src/StockApp.Infrastructure/Migrations/20260608223621_InitialCreate.cs \
       src/StockApp.Infrastructure/Migrations/20260608223621_InitialCreate.Designer.cs \
       src/StockApp.Infrastructure/Migrations/20260609211956_AddCatalogoExtensions.cs \
       src/StockApp.Infrastructure/Migrations/20260609211956_AddCatalogoExtensions.Designer.cs \
       src/StockApp.Infrastructure/Migrations/20260610221527_AddMovimientoStockIndices.cs \
       src/StockApp.Infrastructure/Migrations/20260610221527_AddMovimientoStockIndices.Designer.cs \
       src/StockApp.Infrastructure/Migrations/AppDbContextModelSnapshot.cs
```

- [ ] **Step 2: Generar la migración InitialCreate para Npgsql**

Run:
```bash
dotnet ef migrations add InitialCreate \
  --project src/StockApp.Infrastructure \
  --startup-project src/StockApp.Presentation
```
Expected: `Done.` y tres archivos nuevos en `src/StockApp.Infrastructure/Migrations/` (`<timestamp>_InitialCreate.cs`, `.Designer.cs`, `AppDbContextModelSnapshot.cs`). EF usa el `AppDbContextFactory` (Npgsql) para construir el modelo; no requiere un Postgres corriendo.

- [ ] **Step 3: Verificar que la migración es de Npgsql**

Run: `grep -l "Npgsql:ValueGenerationStrategy\|IdentityByDefaultColumn" src/StockApp.Infrastructure/Migrations/*InitialCreate.cs`
Expected: el archivo aparece (confirma columnas identity de Postgres, no `Autoincrement` de SQLite). Confirmar también que el índice filtrado usa `"CodigoBarras" IS NOT NULL`:
Run: `grep -n "CodigoBarras. IS NOT NULL" src/StockApp.Infrastructure/Migrations/*InitialCreate.cs`
Expected: una línea con el filtro entre comillas dobles.

- [ ] **Step 4: Compilar Infrastructure**

Run: `dotnet build src/StockApp.Infrastructure`
Expected: build correcto.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Infrastructure/Migrations
git commit -m "feat(datos): regenerar migracion InitialCreate para PostgreSQL"
```

---

### Task 3: Arnés de tests contra Postgres real (Testcontainers)

Agrega `Testcontainers.PostgreSql`, crea un fixture de colección que levanta Postgres y aplica migraciones una vez, una clase base que aísla cada test con TRUNCATE, migra todos los tests de repositorio al fixture, y elimina los tests atados a SQLite (migraciones descartadas + smoke in-memory). La reescritura de `MovimientoStockRepositoryTests` deja C1/C2/C6 intactos en comportamiento y prepara C3/C4/C5 para el nuevo contrato de la Task 4.

**Files:**
- Modify: `tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj:12-19`
- Create: `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresFixture.cs`
- Create: `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryTests.cs` (reescritura completa del setup + C4)
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/ProductoRepositoryTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/CategoriaRepositoryTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/ProveedorRepositoryTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/UnidadMedidaRepositoryTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/UsuarioRepositoryTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/ReporteStockRepositoryCategoriaTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/ReporteStockRepositoryMasMovidosTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/ReporteStockRepositoryValorizacionTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/AuditoriaQueryRepositoryTests.cs`
- Delete: `tests/StockApp.Infrastructure.Tests/Migrations/InitialCreateMigrationTests.cs`
- Delete: `tests/StockApp.Infrastructure.Tests/Migrations/AddCatalogoExtensionsMigrationTests.cs`
- Delete: `tests/StockApp.Infrastructure.Tests/Migrations/AddMovimientoStockIndicesMigrationTests.cs`
- Delete: `tests/StockApp.Infrastructure.Tests/AppDbContextSmokeTests.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Persistence/AppDbContextSchemaTests.cs`

**Interfaces:**
- Produces: `PostgresFixture` con `string ConnectionString`, `AppDbContext CrearContexto()`, `Task InitializeAsync()`, `Task DisposeAsync()`; `[CollectionDefinition("Postgres")] PostgresCollection`; `abstract class PostgresRepositoryTestBase(PostgresFixture fixture)` con `protected AppDbContext Context` y `protected PostgresFixture Fixture`.
- Consumes: los nombres de tabla generados en la Task 2.

- [ ] **Step 1: Agregar Testcontainers.PostgreSql al proyecto de tests**

Run: `dotnet add tests/StockApp.Infrastructure.Tests package Testcontainers.PostgreSql`
Expected: se agrega un `<PackageReference Include="Testcontainers.PostgreSql" Version="..." />` al csproj (última versión estable). Dejar el número que resuelva el comando.

- [ ] **Step 2: Crear el fixture de Postgres**

Crear `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresFixture.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace StockApp.Infrastructure.Tests.Fixtures;

/// <summary>
/// Levanta UN contenedor PostgreSQL para toda la colección de tests de Infrastructure
/// y aplica las migraciones una sola vez. Requiere Docker disponible en la máquina.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Crea un AppDbContext nuevo apuntado al contenedor (uno por unidad de trabajo).</summary>
    public AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
```

- [ ] **Step 3: Crear la clase base con aislamiento por TRUNCATE**

Crear `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;
using Xunit;

namespace StockApp.Infrastructure.Tests.Fixtures;

/// <summary>
/// Base para tests de repositorio contra Postgres real. Antes de cada test hace TRUNCATE
/// de todas las tablas con RESTART IDENTITY para aislar el estado y resetear las identidades.
/// </summary>
[Collection("Postgres")]
public abstract class PostgresRepositoryTestBase : IDisposable
{
    protected readonly PostgresFixture Fixture;
    protected readonly AppDbContext Context;

    protected PostgresRepositoryTestBase(PostgresFixture fixture)
    {
        Fixture = fixture;
        LimpiarTablas();
        Context = fixture.CrearContexto();
    }

    public void Dispose() => Context.Dispose();

    private void LimpiarTablas()
    {
        using var ctx = Fixture.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", \"Usuarios\" RESTART IDENTITY CASCADE;");
    }
}
```

> VERIFICACIÓN: los nombres entre comillas deben coincidir EXACTAMENTE con las tablas creadas por la migración de la Task 2. Confirmar con `grep -o "CREATE TABLE \"[A-Za-z]*\"" src/StockApp.Infrastructure/Migrations/*InitialCreate.cs` y ajustar el TRUNCATE si algún nombre difiere.

- [ ] **Step 4: Eliminar los tests atados a migraciones SQLite descartadas y al smoke in-memory**

```bash
git rm tests/StockApp.Infrastructure.Tests/Migrations/InitialCreateMigrationTests.cs \
       tests/StockApp.Infrastructure.Tests/Migrations/AddCatalogoExtensionsMigrationTests.cs \
       tests/StockApp.Infrastructure.Tests/Migrations/AddMovimientoStockIndicesMigrationTests.cs \
       tests/StockApp.Infrastructure.Tests/AppDbContextSmokeTests.cs
```

> Los tres tests de `Migrations/` verificaban migraciones SQLite que ya no existen. `AppDbContextSmokeTests` probaba `EnsureCreated` sobre SQLite in-memory; su rol (el esquema se crea sin error) queda cubierto por `PostgresFixture.InitializeAsync` (aplica `MigrateAsync` al arrancar la colección) y por `AppDbContextSchemaTests` reescrito abajo.

- [ ] **Step 5: Reescribir AppDbContextSchemaTests contra el fixture**

Reemplazar el contenido completo de `tests/StockApp.Infrastructure.Tests/Persistence/AppDbContextSchemaTests.cs` por (los DbSet siguen existiendo; ahora se valida contra el contexto del fixture):

```csharp
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

[Collection("Postgres")]
public class AppDbContextSchemaTests
{
    private readonly PostgresFixture _fixture;

    public AppDbContextSchemaTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public void AppDbContext_ExponeTodosLosDbSet()
    {
        using var ctx = _fixture.CrearContexto();
        Assert.NotNull(ctx.Usuarios);
        Assert.NotNull(ctx.Productos);
        Assert.NotNull(ctx.Categorias);
        Assert.NotNull(ctx.Proveedores);
        Assert.NotNull(ctx.UnidadesMedida);
        Assert.NotNull(ctx.MovimientosStock);
        Assert.NotNull(ctx.LogsAuditoria);
    }
}
```

- [ ] **Step 6: Migrar cada test de repositorio al fixture (recipe uniforme)**

Para cada uno de estos archivos: `ProductoRepositoryTests.cs`, `CategoriaRepositoryTests.cs`, `ProveedorRepositoryTests.cs`, `UnidadMedidaRepositoryTests.cs`, `UsuarioRepositoryTests.cs`, `ReporteStockRepositoryCategoriaTests.cs`, `ReporteStockRepositoryMasMovidosTests.cs`, `ReporteStockRepositoryValorizacionTests.cs`, `AuditoriaQueryRepositoryTests.cs`, aplicar esta transformación (el cuerpo de cada método `[Fact]`/`[Theory]` NO cambia):

1. Agregar `using StockApp.Infrastructure.Tests.Fixtures;`.
2. Quitar `using Microsoft.Data.Sqlite;` y cualquier campo `SqliteConnection`.
3. Cambiar la declaración de clase para heredar de la base y recibir el fixture. Antes (patrón actual, ejemplo de `ProductoRepositoryTests`):

```csharp
public class ProductoRepositoryTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly AppDbContext _ctx;
    private readonly ProductoRepository _repo;

    public ProductoRepositoryTests()
    {
        _connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=...;Mode=Memory;Cache=Shared");
        _connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        _ctx = new AppDbContext(options);
        _ctx.Database.EnsureCreated();
        _repo = new ProductoRepository(_ctx);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _connection.Close();
        _connection.Dispose();
    }
    // ...tests...
}
```

Después:

```csharp
public class ProductoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly ProductoRepository _repo;

    public ProductoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new ProductoRepository(Context);
    }
    // ...tests... (reemplazar cada uso de `_ctx` por `Context`)
}
```

4. Reemplazar en todo el archivo `_ctx` por `Context`.
5. Donde el test abría un segundo contexto sobre la misma conexión para verificación fresca (`new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection)` + `new AppDbContext(...)`), reemplazar por `Fixture.CrearContexto()`.
6. Borrar el método `Dispose()` propio y la `: IDisposable` de la firma (los provee la base).

> RIESGO POSTGRES vs SQLITE (verificar por archivo al correr): Postgres es case-sensitive y ordena strings por collation distinta a SQLite. Los tests de búsqueda por texto (`Contains`, `ToLower`, `StartsWith`) y los de ordenamiento alfabético pueden divergir. Si un test de búsqueda/orden falla tras la migración, es una divergencia de proveedor legítima: ajustar la ASERCIÓN del test (no la lógica del repo) para reflejar el comportamiento de Postgres, salvo que revele un bug real. Registrar en engram cualquier divergencia encontrada.

- [ ] **Step 7: Reescribir el setup de MovimientoStockRepositoryTests (C1, C2, C6)**

Reemplazar en `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryTests.cs` el bloque de `using`s, la firma de clase, el constructor y el `Dispose` (líneas 1-43) por:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests de MovimientoStockRepository contra PostgreSQL real (Testcontainers).
/// Cada test parte de tablas truncadas (PostgresRepositoryTestBase).
/// </summary>
public class MovimientoStockRepositoryTests : PostgresRepositoryTestBase
{
    private readonly MovimientoStockRepository _repo;

    public MovimientoStockRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new MovimientoStockRepository(Context);
    }
```

Luego, en todo el archivo, reemplazar `_ctx` por `Context`, y cada verificación con segundo contexto `new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection)...` por `Fixture.CrearContexto()`. Borrar la clase de cierre `}` sobrante que quedaba tras el viejo `Dispose` (la base ya libera el contexto).

> Ejemplo del cambio de verificación fresca (patrón repetido en C3/C5), reemplazar:
> ```csharp
> var opts2 = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
> await using var ctx2 = new AppDbContext(opts2);
> ```
> por:
> ```csharp
> await using var ctx2 = Fixture.CrearContexto();
> ```

- [ ] **Step 8: Reescribir C4 (rollback atómico) y su repo auxiliar para Postgres**

Reemplazar el test `RegistrarMovimientoAtomicoAsync_DetalleNull_RollbackTotal` y la clase auxiliar `MovimientoStockRepositoryConDetalleNulo` del final del archivo por esta versión, que fuerza `DbUpdateException` con `Detalle=null` (columna NOT NULL) DENTRO de la transacción explícita, verificando que el UPDATE de stock vía `ExecuteUpdateAsync` también se revierte:

```csharp
    // ── C4: Rollback atómico (MANDATORY) ──────────────────────────────────────
    // Fuerza DbUpdateException con Detalle=null (columna NOT NULL) dentro de la
    // transacción explícita; verifica que el UPDATE de stock también se revierte.

    [Fact]
    public async Task RegistrarMovimientoAtomicoAsync_DetalleNull_RollbackTotal()
    {
        var (_, usuario, producto) = await SeedBaseAsync(stockInicial: 50m);
        int productoId = producto.Id;
        int usuarioId  = usuario.Id;

        var repoRoto = new MovimientoStockRepositoryConDetalleNulo(Context);

        var movimiento = new MovimientoStock
        {
            ProductoId     = productoId,
            UsuarioId      = usuarioId,
            Tipo           = TipoMovimiento.Entrada,
            Cantidad       = 20m,
            PrecioUnitario = 5m,
            Fecha          = DateTime.UtcNow,
            Motivo         = MotivoMovimiento.Compra
        };

        var args = new RegistroAtomicoArgs(
            Movimiento:       movimiento,
            ProductoId:       productoId,
            Tipo:             TipoMovimiento.Entrada,
            Cantidad:         20m,
            Forzar:           false,
            UsuarioId:        usuarioId,
            DetalleAuditoria: "se sobreescribe con null en el repo roto");

        await Assert.ThrowsAsync<DbUpdateException>(
            () => repoRoto.RegistrarMovimientoAtomicoAsync(args));

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync());
        var productoFresh = await ctx2.Productos.FindAsync(productoId);
        Assert.Equal(50m, productoFresh!.StockActual);   // stock intacto → rollback del UPDATE
        Assert.Equal(0, await ctx2.LogsAuditoria.CountAsync());
    }
```

Y la clase auxiliar al final del archivo:

```csharp
/// <summary>
/// Variante que inyecta Detalle=null para forzar DbUpdateException DENTRO de la
/// transacción explícita. Replica la estructura del método real (BeginTransaction +
/// ExecuteUpdateAsync + inserts) pero con Detalle inválido, para verificar el rollback
/// completo (incluido el UPDATE de stock). Usada solo en el test C4.
/// </summary>
internal sealed class MovimientoStockRepositoryConDetalleNulo : MovimientoStockRepository
{
    private readonly AppDbContext _ctx;

    public MovimientoStockRepositoryConDetalleNulo(AppDbContext ctx) : base(ctx)
        => _ctx = ctx;

    public override async Task<ResultadoRegistro> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        var delta = args.Tipo == TipoMovimiento.Entrada ? args.Cantidad : -args.Cantidad;
        await _ctx.Productos
            .Where(p => p.Id == args.ProductoId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockActual, p => p.StockActual + delta));

        _ctx.MovimientosStock.Add(args.Movimiento);
        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = args.UsuarioId,
            Fecha     = DateTime.UtcNow,
            Accion    = AccionAuditada.RegistroMovimiento,
            Entidad   = "MovimientoStock",
            EntidadId = args.ProductoId,
            Detalle   = null!   // viola NOT NULL → SaveChangesAsync lanza DbUpdateException
        });

        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();
        return new ResultadoRegistro(ResultadoRegistroEstado.Ok, args.Movimiento.Id, 0m);
    }
}
```

> `RegistroAtomicoArgs` y `ResultadoRegistro` con esta forma se definen en la Task 4. Este archivo NO compilará verde hasta que la Task 4 esté hecha; C4 se ejecuta al final de la Task 4. Es correcto por el acoplamiento del clúster.

- [ ] **Step 9: Ajustar C3 y C5 al nuevo contrato tipado**

En el test C3 `RegistrarMovimientoAtomicoAsync_DatosValidos_PersisteTresRegistros`, actualizar la construcción de `args` y la aserción del retorno:

```csharp
        var args = new RegistroAtomicoArgs(
            Movimiento:       movimiento,
            ProductoId:       productoId,
            Tipo:             TipoMovimiento.Entrada,
            Cantidad:         20m,
            Forzar:           false,
            UsuarioId:        usuarioId,
            DetalleAuditoria: "ProductoId=1; Tipo=Entrada; Cantidad=20; StockAnterior=50; StockNuevo=70");

        var resultado = await _repo.RegistrarMovimientoAtomicoAsync(args);

        await using var ctx2 = Fixture.CrearContexto();

        Assert.Equal(ResultadoRegistroEstado.Ok, resultado.Estado);
        Assert.True(resultado.MovimientoId > 0, "Debe retornar el Id generado del movimiento");
        Assert.Equal(70m, resultado.StockResultante);
        Assert.Equal(1, await ctx2.MovimientosStock.CountAsync());
        var productoFresh = await ctx2.Productos.FindAsync(productoId);
        Assert.Equal(70m, productoFresh!.StockActual);
        Assert.Equal(1, await ctx2.LogsAuditoria.CountAsync());
        var log = await ctx2.LogsAuditoria.FirstAsync();
        Assert.Equal(17, (int)log.Accion);
```

C5 (`RecalcularAtomicoAsync_...`) usa `RecalculoAtomicoArgs`, que NO cambia; solo requiere el reemplazo de `_ctx`→`Context` y del segundo contexto por `Fixture.CrearContexto()` ya aplicado en el Step 7. No tocar su lógica.

- [ ] **Step 10: Verificar que el proyecto de tests compila (aún rojo en runtime hasta Task 4)**

Run: `dotnet build tests/StockApp.Infrastructure.Tests`
Expected: FAIL de compilación con errores en los símbolos `RegistroAtomicoArgs(... Tipo/Cantidad/Forzar ...)`, `ResultadoRegistro` y `ResultadoRegistroEstado` (aún no existen). Esto CONFIRMA el acoplamiento con la Task 4: el arnés ya referencia el contrato nuevo. Los tests de repositorio no-movimientos deben compilar; los errores deben limitarse a `MovimientoStockRepositoryTests.cs`.

- [ ] **Step 11: Commit**

```bash
git add tests/StockApp.Infrastructure.Tests
git commit -m "test(datos): arnes Testcontainers PostgreSQL y migracion de tests de repositorio"
```

---

### Task 4: Transacción atómica con UPDATE condicional + resultado tipado

Cambia el contrato de `RegistroAtomicoArgs` para pasar `Tipo`/`Cantidad`/`Forzar` en vez del `StockNuevo` absoluto, agrega el resultado tipado `ResultadoRegistro`/`ResultadoRegistroEstado`, implementa la transacción explícita con `ExecuteUpdateAsync` condicional en el repo, y actualiza `MovimientoStockService` para traducir el resultado a `StockInsuficienteException`.

**Files:**
- Modify: `src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs:10-15,47`
- Modify: `src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs:34-61`
- Modify: `src/StockApp.Application/Movimientos/MovimientoStockService.cs:58-101`
- Modify: `tests/StockApp.Application.Tests/Movimientos/MovimientoStockServiceTests.cs` (mocks del nuevo contrato)
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryTests.cs` (C3/C4/C5 de la Task 3 + nuevos casos de guard)

**Interfaces:**
- Produces: `record RegistroAtomicoArgs(MovimientoStock Movimiento, int ProductoId, TipoMovimiento Tipo, decimal Cantidad, bool Forzar, int UsuarioId, string DetalleAuditoria)`; `enum ResultadoRegistroEstado { Ok, StockInsuficiente }`; `record ResultadoRegistro(ResultadoRegistroEstado Estado, int MovimientoId, decimal StockResultante)`; `Task<ResultadoRegistro> IMovimientoStockRepository.RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args)`.
- Consumes: `StockInsuficienteException(int productoId, decimal stockActual, decimal cantidadSolicitada)` (sin cambios); `TipoMovimiento`, `MotivoMovimiento`, `AccionAuditada` (sin cambios).

- [ ] **Step 1: Escribir el test de repo del guard condicional (falla)**

Agregar a `MovimientoStockRepositoryTests.cs` estos dos tests (Postgres real):

```csharp
    // ── Guard condicional atómico ─────────────────────────────────────────────

    [Fact]
    public async Task RegistrarMovimientoAtomicoAsync_SalidaSinStock_NoModificaNada()
    {
        var (_, usuario, producto) = await SeedBaseAsync(stockInicial: 5m);

        var movimiento = new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Salida,
            Cantidad = 10m, PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Venta
        };
        var args = new RegistroAtomicoArgs(
            Movimiento: movimiento, ProductoId: producto.Id, Tipo: TipoMovimiento.Salida,
            Cantidad: 10m, Forzar: false, UsuarioId: usuario.Id, DetalleAuditoria: "salida sin stock");

        var resultado = await _repo.RegistrarMovimientoAtomicoAsync(args);

        Assert.Equal(ResultadoRegistroEstado.StockInsuficiente, resultado.Estado);
        Assert.Equal(0, resultado.MovimientoId);
        Assert.Equal(5m, resultado.StockResultante);   // stock actual sin tocar

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(0, await ctx2.MovimientosStock.CountAsync());
        Assert.Equal(0, await ctx2.LogsAuditoria.CountAsync());
        var p = await ctx2.Productos.FindAsync(producto.Id);
        Assert.Equal(5m, p!.StockActual);
    }

    [Fact]
    public async Task RegistrarMovimientoAtomicoAsync_SalidaForzada_PermiteNegativo()
    {
        var (_, usuario, producto) = await SeedBaseAsync(stockInicial: 5m);

        var movimiento = new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuario.Id, Tipo = TipoMovimiento.Salida,
            Cantidad = 8m, PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Merma
        };
        var args = new RegistroAtomicoArgs(
            Movimiento: movimiento, ProductoId: producto.Id, Tipo: TipoMovimiento.Salida,
            Cantidad: 8m, Forzar: true, UsuarioId: usuario.Id, DetalleAuditoria: "salida forzada");

        var resultado = await _repo.RegistrarMovimientoAtomicoAsync(args);

        Assert.Equal(ResultadoRegistroEstado.Ok, resultado.Estado);
        Assert.Equal(-3m, resultado.StockResultante);   // 5 - 8 = -3, permitido con Forzar

        await using var ctx2 = Fixture.CrearContexto();
        Assert.Equal(1, await ctx2.MovimientosStock.CountAsync());
        Assert.Equal(1, await ctx2.LogsAuditoria.CountAsync());
    }
```

- [ ] **Step 2: Cambiar el contrato en IMovimientoStockRepository (hace fallar la compilación de forma dirigida)**

En `src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs`, reemplazar el record `RegistroAtomicoArgs` (líneas 10-15) por el nuevo contrato y agregar el resultado tipado justo debajo:

```csharp
public record RegistroAtomicoArgs(
    MovimientoStock Movimiento,   // entidad ya construida (sin Id)
    int ProductoId,
    TipoMovimiento Tipo,          // define el signo del delta
    decimal Cantidad,             // siempre positiva
    bool Forzar,                  // true → permite stock negativo (bypass del guard condicional)
    int UsuarioId,
    string DetalleAuditoria);     // payload listo para LogAuditoria.Detalle

/// <summary>Estado del intento de registro atómico.</summary>
public enum ResultadoRegistroEstado
{
    Ok,
    StockInsuficiente
}

/// <summary>
/// Resultado tipado del registro atómico. Ok → movimiento persistido; StockInsuficiente →
/// el UPDATE condicional afectó 0 filas (imposible lost-update, imposible negativo sin forzar).
/// StockResultante = stock tras el update (Ok) o stock actual sin tocar (StockInsuficiente).
/// </summary>
public record ResultadoRegistro(
    ResultadoRegistroEstado Estado,
    int MovimientoId,
    decimal StockResultante);
```

Agregar el `using StockApp.Domain.Enums;` al inicio del archivo (para `TipoMovimiento`). Cambiar la firma en la interfaz (línea 47) de `Task<int> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args);` a:

```csharp
    Task<ResultadoRegistro> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args);
```

- [ ] **Step 3: Implementar la transacción atómica en el repositorio**

En `src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs`, reemplazar el método `RegistrarMovimientoAtomicoAsync` completo (líneas 34-61) por:

```csharp
    /// <inheritdoc/>
    /// ATÓMICO: transacción explícita que envuelve un UPDATE CONDICIONAL de stock
    /// (la base serializa la fila y hace cumplir "no negativo"), el insert del movimiento
    /// y el insert del LogAuditoria. Para salidas sin forzar, 0 filas afectadas ⇒ StockInsuficiente
    /// (rollback, no se inserta nada). Entradas y salidas forzadas aplican el delta sin guard.
    public virtual async Task<ResultadoRegistro> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs args)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        if (args.Tipo == TipoMovimiento.Salida && !args.Forzar)
        {
            // UPDATE condicional atómico: solo baja si hay stock suficiente
            var filas = await _ctx.Productos
                .Where(p => p.Id == args.ProductoId && p.StockActual >= args.Cantidad)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockActual, p => p.StockActual - args.Cantidad));

            if (filas == 0)
            {
                // 0 filas: stock insuficiente O producto inexistente. Distinguir (el producto
                // usa baja lógica, así que la fila persiste; 0 filas normalmente = insuficiente).
                var stockActual = await _ctx.Productos
                    .Where(p => p.Id == args.ProductoId)
                    .Select(p => (decimal?)p.StockActual)
                    .FirstOrDefaultAsync();

                if (stockActual is null)
                    throw new KeyNotFoundException($"Producto {args.ProductoId} no encontrado.");

                await tx.RollbackAsync();
                return new ResultadoRegistro(ResultadoRegistroEstado.StockInsuficiente, 0, stockActual.Value);
            }
        }
        else
        {
            // Entrada, o salida forzada (permite negativo): delta con signo, sin guard
            var delta = args.Tipo == TipoMovimiento.Entrada ? args.Cantidad : -args.Cantidad;
            var filas = await _ctx.Productos
                .Where(p => p.Id == args.ProductoId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.StockActual, p => p.StockActual + delta));

            if (filas == 0)
                throw new KeyNotFoundException($"Producto {args.ProductoId} no encontrado.");
        }

        // Insert movimiento + log dentro de la MISMA transacción
        _ctx.MovimientosStock.Add(args.Movimiento);
        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId = args.UsuarioId,
            Fecha     = DateTime.UtcNow,
            Accion    = AccionAuditada.RegistroMovimiento,   // 17
            Entidad   = "MovimientoStock",
            EntidadId = args.ProductoId,
            Detalle   = args.DetalleAuditoria
        });
        await _ctx.SaveChangesAsync();

        await tx.CommitAsync();

        // Stock resultante autoritativo (proyección escalar → lee de la BD, no del change tracker)
        var stockResultante = await _ctx.Productos
            .Where(p => p.Id == args.ProductoId)
            .Select(p => p.StockActual)
            .FirstAsync();

        return new ResultadoRegistro(ResultadoRegistroEstado.Ok, args.Movimiento.Id, stockResultante);
    }
```

> `RecalcularAtomicoAsync` (líneas 66-84) NO se toca: es un recálculo absoluto (Σ movimientos), no un delta concurrente; conserva su patrón de SaveChanges único.

- [ ] **Step 4: Actualizar MovimientoStockService para traducir el resultado tipado**

En `src/StockApp.Application/Movimientos/MovimientoStockService.cs`, reemplazar el bloque B6+B7 (líneas 58-101) por:

```csharp
        // B6: cálculo de signo (el guard de stock lo hace el UPDATE condicional en el repo)
        var stockAnterior = producto.StockActual;
        var delta         = dto.Tipo == TipoMovimiento.Entrada ? dto.Cantidad : -dto.Cantidad;
        var stockNuevo    = stockAnterior + delta;

        // B7: componer entidad + args + llamada atómica
        var movimiento = new MovimientoStock
        {
            ProductoId    = dto.ProductoId,
            UsuarioId     = _session.UsuarioActual!.Id,
            Tipo          = dto.Tipo,
            Motivo        = dto.Motivo,
            Cantidad      = dto.Cantidad,
            PrecioUnitario = dto.PrecioUnitario ?? 0m,
            Fecha         = DateTime.UtcNow,
            Comentario    = dto.Comentario
        };

        var detalle = $"ProductoId={dto.ProductoId}; Tipo={dto.Tipo}; Motivo={dto.Motivo}; " +
                      $"Cantidad={dto.Cantidad}; StockAnterior={stockAnterior}; StockNuevo={stockNuevo}";

        var args = new RegistroAtomicoArgs(
            Movimiento:       movimiento,
            ProductoId:       dto.ProductoId,
            Tipo:             dto.Tipo,
            Cantidad:         dto.Cantidad,
            Forzar:           forzar,
            UsuarioId:        _session.UsuarioActual!.Id,
            DetalleAuditoria: detalle);

        var resultado = await _repo.RegistrarMovimientoAtomicoAsync(args);

        // El guard "no negativo" lo hace la BD dentro de la transacción; acá se traduce
        // el resultado tipado a la excepción de dominio (respetando forzar, que ya viajó en args).
        if (resultado.Estado == ResultadoRegistroEstado.StockInsuficiente)
            throw new StockInsuficienteException(dto.ProductoId, resultado.StockResultante, dto.Cantidad);

        return new MovimientoRegistradoDto(
            MovimientoId:  resultado.MovimientoId,
            ProductoId:    dto.ProductoId,
            Tipo:          dto.Tipo,
            Motivo:        dto.Motivo,
            Cantidad:      dto.Cantidad,
            PrecioUnitario: dto.PrecioUnitario ?? 0m,
            StockAnterior: resultado.StockResultante - delta,
            StockNuevo:    resultado.StockResultante,
            Fecha:         movimiento.Fecha);
```

> Se elimina el guard en memoria (viejas líneas 63-64 `if (dto.Tipo == Salida && stockNuevo < 0 && !forzar) throw ...`). Agregar `using StockApp.Application.Interfaces;` si no está (ya está: línea 2). `ResultadoRegistroEstado` vive en ese namespace.

- [ ] **Step 5: Actualizar los mocks del service test al nuevo contrato**

En `tests/StockApp.Application.Tests/Movimientos/MovimientoStockServiceTests.cs`, actualizar cada `repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(...)).ReturnsAsync(<int>)` para devolver un `ResultadoRegistro`, y ajustar el test de stock insuficiente. Cambios exactos:

En `RegistrarAsync_SalidaStockInsuficiente_LanzaStockInsuficienteException` (líneas 180-191) — ahora el repo SÍ se invoca y devuelve StockInsuficiente:

```csharp
    [Fact]
    public async Task RegistrarAsync_SalidaStockInsuficiente_LanzaStockInsuficienteException()
    {
        var (svc, repo, _, _) = Crear();
        var producto = ProductoActivo(stock: 3m);
        repo.Setup(r => r.ObtenerProductoAsync(1)).ReturnsAsync(producto);
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(new ResultadoRegistro(ResultadoRegistroEstado.StockInsuficiente, 0, 3m));

        var dto = DtoSalida(cantidad: 10m);

        await Assert.ThrowsAsync<StockInsuficienteException>(() => svc.RegistrarAsync(dto));
        repo.Verify(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()), Times.Once);
    }
```

En `RegistrarAsync_SalidaForzar_PermiteStockNegativoYLlamaRepo` (líneas 193-208):

```csharp
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(new ResultadoRegistro(ResultadoRegistroEstado.Ok, 42, -7m));
```
(el `Assert.Equal(-7m, result.StockNuevo);` sigue válido porque StockNuevo = StockResultante).

En `RegistrarAsync_Entrada_SumaAlStock` (líneas 210-224):
```csharp
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(new ResultadoRegistro(ResultadoRegistroEstado.Ok, 7, 15m));
```
(asserts `StockNuevo=15`, `StockAnterior=10` siguen válidos: 15 - (+5) = 10).

En `RegistrarAsync_EntradaExitosa_RetornaMovimientoRegistradoDto` (líneas 228-250):
```csharp
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(new ResultadoRegistro(ResultadoRegistroEstado.Ok, 99, 15m));
```
(asserts existentes válidos: StockAnterior=5, StockNuevo=15).

En `RegistrarAsync_SalidaExitosa_RetornaMovimientoRegistradoDto` (líneas 252-267):
```csharp
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(new ResultadoRegistro(ResultadoRegistroEstado.Ok, 100, 12m));
```
(asserts StockAnterior=20, StockNuevo=12: 12 - (-8) = 20).

En `RegistrarAsync_LlamaAtomicoExactamenteUnaVez` (líneas 269-281):
```csharp
        repo.Setup(r => r.RegistrarMovimientoAtomicoAsync(It.IsAny<RegistroAtomicoArgs>()))
            .ReturnsAsync(new ResultadoRegistro(ResultadoRegistroEstado.Ok, 1, 25m));
```

Agregar `using StockApp.Application.Interfaces;` al archivo si falta (ya está: línea 3).

- [ ] **Step 6: Correr los tests de service (Application.Tests) y verificar verde**

Run: `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~MovimientoStockServiceTests`
Expected: PASS (todos). No requiere Docker (usa mocks).

- [ ] **Step 7: Correr los tests de repo (Infrastructure.Tests) contra Postgres y verificar verde**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~MovimientoStockRepositoryTests`
Expected: PASS, incluidos C3, C4 (rollback), C5, el guard condicional y la salida forzada. REQUIERE Docker corriendo (Testcontainers levanta postgres:16-alpine).

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Application/Interfaces/IMovimientoStockRepository.cs \
        src/StockApp.Infrastructure/Repositories/MovimientoStockRepository.cs \
        src/StockApp.Application/Movimientos/MovimientoStockService.cs \
        tests/StockApp.Application.Tests/Movimientos/MovimientoStockServiceTests.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockRepositoryTests.cs
git commit -m "feat(stock): transaccion atomica con UPDATE condicional y resultado tipado"
```

---

### Task 5: Test de concurrencia contra Postgres real

Prueba el corazón del diseño (§5): dos salidas simultáneas (`Task.WhenAll`) sobre el mismo producto con stock limitado → exactamente una tiene éxito, stock nunca negativo, sin lost-update.

**Files:**
- Create: `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockConcurrenciaTests.cs`

**Interfaces:**
- Consumes: `MovimientoStockRepository`, `RegistroAtomicoArgs`, `ResultadoRegistro`, `ResultadoRegistroEstado`, `PostgresRepositoryTestBase`, `PostgresFixture`.

- [ ] **Step 1: Escribir el test de concurrencia (falla si el guard no es atómico)**

Crear `tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockConcurrenciaTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Test de concurrencia MANDATORY (design §5): dos salidas simultáneas sobre el mismo
/// producto con stock limitado. Exactamente una tiene éxito; el stock nunca queda negativo;
/// no hay lost-update. Cada tarea usa su PROPIO AppDbContext (un DbContext no es thread-safe).
/// Requiere Docker (Testcontainers PostgreSQL).
/// </summary>
public class MovimientoStockConcurrenciaTests : PostgresRepositoryTestBase
{
    public MovimientoStockConcurrenciaTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task DosSalidasSimultaneas_MismoProducto_UnaSolaTieneExito()
    {
        // Seed: stock=10, cada salida pide 8 → solo una puede pasar
        var um = new UnidadMedida { Nombre = "Unidad", Abreviatura = "u" };
        var usuario = new Usuario
        {
            NombreUsuario = "conc_user", HashContrasena = "hash",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        };
        Context.UnidadesMedida.Add(um);
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = "CONC001", Nombre = "Producto Concurrente", UnidadMedida = um,
            PrecioCosto = 10m, PrecioVenta = 20m, StockActual = 10m, Activo = true, FechaAlta = DateTime.UtcNow
        };
        Context.Productos.Add(producto);
        await Context.SaveChangesAsync();
        int productoId = producto.Id;
        int usuarioId = usuario.Id;

        // Cada tarea con su propio contexto y repo (aislamiento de conexión)
        async Task<ResultadoRegistro> SalidaAsync()
        {
            await using var ctx = Fixture.CrearContexto();
            var repo = new MovimientoStockRepository(ctx);
            var mov = new MovimientoStock
            {
                ProductoId = productoId, UsuarioId = usuarioId, Tipo = TipoMovimiento.Salida,
                Cantidad = 8m, PrecioUnitario = 5m, Fecha = DateTime.UtcNow, Motivo = MotivoMovimiento.Venta
            };
            var args = new RegistroAtomicoArgs(
                Movimiento: mov, ProductoId: productoId, Tipo: TipoMovimiento.Salida,
                Cantidad: 8m, Forzar: false, UsuarioId: usuarioId, DetalleAuditoria: "salida concurrente");
            return await repo.RegistrarMovimientoAtomicoAsync(args);
        }

        var resultados = await Task.WhenAll(SalidaAsync(), SalidaAsync());

        // Exactamente una Ok y una StockInsuficiente
        Assert.Equal(1, resultados.Count(r => r.Estado == ResultadoRegistroEstado.Ok));
        Assert.Equal(1, resultados.Count(r => r.Estado == ResultadoRegistroEstado.StockInsuficiente));

        await using var verify = Fixture.CrearContexto();
        var stockFinal = await verify.Productos
            .Where(p => p.Id == productoId).Select(p => p.StockActual).FirstAsync();
        Assert.Equal(2m, stockFinal);                       // 10 - 8, nunca negativo, sin lost-update
        Assert.Equal(1, await verify.MovimientosStock.CountAsync());   // solo la salida ganadora
        Assert.Equal(1, await verify.LogsAuditoria.CountAsync());
    }
}
```

- [ ] **Step 2: Correr el test de concurrencia y verificar verde**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~MovimientoStockConcurrenciaTests`
Expected: PASS. Confirma que el UPDATE condicional serializa la fila (una salida gana, la otra ve 0 filas → StockInsuficiente), stock final = 2, un solo movimiento. REQUIERE Docker.

- [ ] **Step 3: Commit**

```bash
git add tests/StockApp.Infrastructure.Tests/Repositories/MovimientoStockConcurrenciaTests.cs
git commit -m "test(stock): concurrencia de salidas simultaneas contra PostgreSQL"
```

---

### Task 6: Reescribir DatabaseInitializer (sin backup file-based)

Quita el backup file-based (concepto SQLite; el reemplazo por `pg_dump` queda fuera de alcance de Fase 1 por design §7) y conserva `MigrateAsync`. Reescribe sus tests contra Postgres.

**Files:**
- Modify: `src/StockApp.Infrastructure/Services/DatabaseInitializer.cs:6-37`
- Modify: `tests/StockApp.Infrastructure.Tests/Services/DatabaseInitializerTests.cs` (reescritura completa)

**Interfaces:**
- Produces: `DatabaseInitializer(AppDbContext context)` con `Task InicializarAsync()` que solo aplica `MigrateAsync`.
- Consumes: `PostgresFixture`.

- [ ] **Step 1: Reescribir el test del initializer (falla al compilar contra la firma vieja)**

Reemplazar el contenido completo de `tests/StockApp.Infrastructure.Tests/Services/DatabaseInitializerTests.cs` por:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Services;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Services;

/// <summary>
/// DatabaseInitializer contra PostgreSQL real: aplica migraciones sin error y deja
/// el esquema al día. El backup file-based se removió (concepto SQLite; el reemplazo
/// por pg_dump es Fase posterior). Requiere Docker (Testcontainers).
/// </summary>
[Collection("Postgres")]
public class DatabaseInitializerTests
{
    private readonly PostgresFixture _fixture;

    public DatabaseInitializerTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task InicializarAsync_AplicaMigraciones_SinError()
    {
        using var ctx = _fixture.CrearContexto();
        var initializer = new DatabaseInitializer(ctx);

        var ex = await Record.ExceptionAsync(() => initializer.InicializarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task InicializarAsync_DejaMigracionesAlDia()
    {
        using var ctx = _fixture.CrearContexto();
        var initializer = new DatabaseInitializer(ctx);

        await initializer.InicializarAsync();

        var pendientes = await ctx.Database.GetPendingMigrationsAsync();
        Assert.Empty(pendientes);
    }
}
```

- [ ] **Step 2: Reescribir DatabaseInitializer**

Reemplazar el contenido completo de `src/StockApp.Infrastructure/Services/DatabaseInitializer.cs` por:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Services;

public class DatabaseInitializer
{
    private readonly AppDbContext _context;

    public DatabaseInitializer(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Aplica las migraciones pendientes al arrancar. El backup file-based (SQLite) se removió;
    /// el respaldo server-side vía pg_dump queda fuera de alcance de Fase 1 (design §7).
    /// </summary>
    public async Task InicializarAsync()
    {
        await _context.Database.MigrateAsync();
    }
}
```

- [ ] **Step 3: Ajustar el registro DI del initializer en App.axaml.cs**

En `src/StockApp.Presentation/App.axaml.cs`, el registro `services.AddTransient<DatabaseInitializer>();` (línea 152) sigue siendo válido (el contenedor resuelve el nuevo ctor de un solo parámetro). No requiere cambio. Verificar que compila en el build de la Task 7.

> RIESGO / DECISIÓN ABIERTA (fuera de alcance de esta tarea, flag para el implementador): `BackupService`/`BackupPeriodicoService` copian el archivo SQLite. Con Postgres ese concepto queda muerto y `BackupPeriodicoService.IniciarAsync()` (invocado en `App.axaml.cs:89`) podría fallar en runtime al no existir el `.db`. El design §7 difiere el respaldo a `pg_dump` server-side (Fase 4). No se toca en Fase 1 salvo que el arranque contra Postgres lo requiera; si el implementador confirma un crash de arranque, neutralizar el `BackupPeriodicoService` es un cambio menor a evaluar aparte.

- [ ] **Step 4: Correr los tests del initializer**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~DatabaseInitializerTests`
Expected: PASS. REQUIERE Docker.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Infrastructure/Services/DatabaseInitializer.cs \
        tests/StockApp.Infrastructure.Tests/Services/DatabaseInitializerTests.cs
git commit -m "refactor(datos): DatabaseInitializer sin backup file-based (solo Migrate)"
```

---

### Task 7: Cierre — DI, limpieza SQLite y suite completa en verde

Verifica/ajusta `ComposicionDITests`, elimina cualquier referencia SQLite remanente, compila la solución y corre la suite completa.

**Files:**
- Modify (si aplica): `tests/StockApp.Infrastructure.Tests/DI/ComposicionDITests.cs`
- Verify: toda la solución.

**Interfaces:**
- Consumes: todo lo producido en Tareas 1-6.

- [ ] **Step 1: Buscar referencias SQLite remanentes en todo el repo**

Run: `grep -rn "UseSqlite\|Microsoft.Data.Sqlite\|EntityFrameworkCore.Sqlite\|SQLitePCLRaw" src tests --include=*.cs --include=*.csproj`
Expected: sin resultados. Si aparece alguno, eliminarlo (era un uso no cubierto por las tareas anteriores).

- [ ] **Step 2: Verificar ComposicionDITests**

Leer `tests/StockApp.Infrastructure.Tests/DI/ComposicionDITests.cs`. Su cableado (IUserDataPathProvider, BackupService, BackupPeriodicoService) no depende de SQLite ni del DbContext, y el stub de `DatabaseInitializer` lanza `InvalidOperationException` (no construye el ctor). Por lo tanto NO requiere cambios funcionales. Confirmar que compila y pasa:

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~ComposicionDITests`
Expected: PASS (3 tests). Si por el cambio de firma de `DatabaseInitializer` el comentario del stub quedó desfasado, actualizar solo el texto del comentario; no cambiar la lógica.

- [ ] **Step 3: Build de la solución completa**

Run: `dotnet build`
Expected: build correcto de todos los proyectos, sin warnings NU1903 (la deuda de SQLitePCLRaw desapareció al quitar el paquete).

- [ ] **Step 4: Correr la suite completa**

Run: `dotnet test`
Expected: todos los proyectos verdes. Los tests de `StockApp.Infrastructure.Tests` REQUIEREN Docker (Testcontainers); asegurarse de que el daemon esté corriendo. Application/Domain/Presentation tests no requieren Docker.

- [ ] **Step 5: Commit (si hubo cambios)**

```bash
git add -A
git commit -m "chore(datos): cierre Fase 1 — limpieza SQLite y suite verde sobre PostgreSQL"
```

---

## Self-Review

**1. Cobertura vs decisiones tomadas:**
- Switch TOTAL a Postgres (decisión #1): Task 1 (paquetes + UseNpgsql en factory/App), Task 3 (tests), Task 7 Step 1 (barrido de remanentes). ✔
- Descartar 3 migraciones SQLite + una `InitialCreate` Postgres (decisión #2): Task 2. ✔
- Transacción atómica con UPDATE condicional (decisión #3, design §5): Task 4 Step 3, patrón `ExecuteUpdateAsync` con `WHERE ... >= @cant`; `forzar=true` → rama else sin guard (permite negativo). ✔
- Tests de concurrencia contra Postgres real (decisión #4): Task 5, `Task.WhenAll` de dos salidas, cada una con su contexto. ✔
- Contrato: pasar delta/cantidad+tipo en vez de `StockNuevo` absoluto + resultado tipado (decisión #5): Task 4 Steps 2-4; `RegistroAtomicoArgs` con `Tipo`/`Cantidad`/`Forzar`, `ResultadoRegistro`/`ResultadoRegistroEstado`, traducción a `StockInsuficienteException` en el service. ✔
- `DatabaseInitializer` reescrito sin backup file-based (design §7): Task 6. ✔
- `AppDbContext` HasFilter a sintaxis Npgsql: Task 1 Step 3. ✔
- `ConnectionStrings:Default` en appsettings + UseNpgsql desde config, Transient conservado: Task 1 Steps 5-6. ✔
- Testcontainers en Infrastructure.Tests + `ComposicionDITests`: Tasks 3 y 7. ✔

**2. Scan de placeholders:** No hay "TODO", "similar a", "manejar errores" ni "etc." como acción. La migración de los 9 tests de repositorio (Task 3 Step 6) es un recipe mecánico con before/after concreto sobre la clase base compartida (no es un placeholder: el cuerpo de cada test es código existente que no cambia; solo se reemplaza el boilerplate de conexión). Las divergencias Postgres/SQLite en búsqueda/orden se marcan como verificación TDD explícita (ajustar aserción), no como trabajo indefinido. Decisiones abiertas marcadas: versión de `Npgsql.EntityFrameworkCore.PostgreSQL` (`10.*` a confirmar), versión de `Testcontainers.PostgreSql` (la que resuelva `dotnet add package`), nombres de tabla del TRUNCATE (verificar contra la migración generada), y el riesgo de `BackupPeriodicoService` en runtime.

**3. Consistencia de tipos/firmas:** `RegistroAtomicoArgs(MovimientoStock, int ProductoId, TipoMovimiento Tipo, decimal Cantidad, bool Forzar, int UsuarioId, string DetalleAuditoria)` se usa idéntico en: la definición (Task 4 Step 2), el repo (Task 4 Step 3, consume `args.Tipo`/`args.Cantidad`/`args.Forzar`/`args.ProductoId`/`args.UsuarioId`/`args.DetalleAuditoria`/`args.Movimiento`), el service (Task 4 Step 4), el arnés de MovimientoStockRepositoryTests (Task 3 Steps 8-9, Task 4 Step 1), el repo auxiliar C4 (Task 3 Step 8) y el test de concurrencia (Task 5). `ResultadoRegistro(ResultadoRegistroEstado Estado, int MovimientoId, decimal StockResultante)` y el enum `{ Ok, StockInsuficiente }` se usan consistentes en repo, service y todos los tests. La firma de interfaz `Task<ResultadoRegistro> RegistrarMovimientoAtomicoAsync(RegistroAtomicoArgs)` coincide con el `override` del repo auxiliar (Task 3 Step 8) y la implementación real (Task 4 Step 3). `RecalculoAtomicoArgs` queda intacto. `StockInsuficienteException(int, decimal, decimal)` se invoca con `(dto.ProductoId, resultado.StockResultante, dto.Cantidad)`, compatible con su ctor existente. `PostgresRepositoryTestBase(PostgresFixture)` con `Context`/`Fixture` protegidos coincide con todos los constructores de tests que hacen `: base(fixture)`.
</content>
</invoke>
