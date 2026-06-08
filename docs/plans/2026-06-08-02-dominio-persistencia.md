# Incremento 2: Dominio + Persistencia + Backups — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Definir todas las entidades del dominio, cablear el `AppDbContext` con Fluent API,
generar la primera migración real que crea el esquema completo, ubicar la base de datos en
el directorio de datos del usuario, inyectar las dependencias en el arranque de Avalonia,
y tener un servicio de backup operativo (pre-migración + periódico cada 12 h con retención
7 días).

**Architecture:** Las entidades viven en `StockApp.Domain`; el `AppDbContext`, la migración,
el servicio de backup y la ubicación de la BD están en `StockApp.Infrastructure`; el cableado
de DI se hace en `StockApp.Presentation` (punto de composición). Los tests de Infrastructure
usan SQLite en memoria o archivo temporal; los tests de Domain son unitarios puros.

**Tech Stack:** .NET 10, C#, EF Core 10 + SQLite, xUnit.

---

## File Structure

```
src/
  StockApp.Domain/
    Entities/
      Usuario.cs
      Producto.cs
      Categoria.cs
      Proveedor.cs
      UnidadMedida.cs
      MovimientoStock.cs
      LogAuditoria.cs
    Enums/
      RolUsuario.cs
      TipoMovimiento.cs
      MotivoMovimiento.cs
      AccionAuditada.cs
  StockApp.Infrastructure/
    Persistence/
      AppDbContext.cs              # DbSet + OnModelCreating (Fluent API)
    Migrations/                   # generado por EF tooling
    Services/
      BackupService.cs
      DatabaseInitializer.cs      # aplica migración + backup pre-migración
    Platform/
      IUserDataPathProvider.cs    # abstracción de directorio de datos por OS
      UserDataPathProvider.cs
  StockApp.Presentation/
    App.axaml.cs                  # registro de DI
tests/
  StockApp.Domain.Tests/
    Entities/
      UsuarioTests.cs
      ProductoTests.cs
      MovimientoStockTests.cs
  StockApp.Infrastructure.Tests/
    Persistence/
      AppDbContextSchemaTests.cs
    Services/
      BackupServiceTests.cs
      DatabaseInitializerTests.cs
    Platform/
      UserDataPathProviderTests.cs
```

Responsabilidad de cada unidad nueva en este incremento:
- `Entities/` + `Enums/`: el modelo de dominio según §4 del spec. Sin lógica de infraestructura.
- `AppDbContext`: agrega los `DbSet<>` y configura mapeo (índices únicos, precisión decimal, FKs, baja lógica).
- `Migrations/`: primera migración (`InitialCreate`) generada con `dotnet ef`. Forward-only.
- `BackupService`: copia `.db` con timestamp, aplica retención (7 días / conserva mínimo 1).
- `DatabaseInitializer`: orquesta backup pre-migración → `Database.Migrate()` al arrancar.
- `IUserDataPathProvider` / `UserDataPathProvider`: abstrae `%LOCALAPPDATA%\StockApp\` (Windows) y `~/.local/share/StockApp/` (Linux).

---

## Task 1: Entidades del dominio y enums

**Files:**
- Create: `src/StockApp.Domain/Entities/Usuario.cs`
- Create: `src/StockApp.Domain/Entities/Producto.cs`
- Create: `src/StockApp.Domain/Entities/Categoria.cs`
- Create: `src/StockApp.Domain/Entities/Proveedor.cs`
- Create: `src/StockApp.Domain/Entities/UnidadMedida.cs`
- Create: `src/StockApp.Domain/Entities/MovimientoStock.cs`
- Create: `src/StockApp.Domain/Entities/LogAuditoria.cs`
- Create: `src/StockApp.Domain/Enums/RolUsuario.cs`
- Create: `src/StockApp.Domain/Enums/TipoMovimiento.cs`
- Create: `src/StockApp.Domain/Enums/MotivoMovimiento.cs`
- Create: `src/StockApp.Domain/Enums/AccionAuditada.cs`

### Tests primero

- [ ] **Step 1: Escribir tests de dominio que fallan**

Create `tests/StockApp.Domain.Tests/Entities/UsuarioTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class UsuarioTests
{
    [Fact]
    public void Usuario_Nuevo_TieneActivoEnTrue()
    {
        var usuario = new Usuario
        {
            NombreUsuario = "admin",
            HashContrasena = "hash",
            Rol = RolUsuario.Admin,
            FechaAlta = DateTime.UtcNow
        };

        Assert.True(usuario.Activo);
    }

    [Fact]
    public void Usuario_NombreCompleto_EsOpcional()
    {
        var usuario = new Usuario
        {
            NombreUsuario = "operador1",
            HashContrasena = "hash",
            Rol = RolUsuario.Operador,
            FechaAlta = DateTime.UtcNow
        };

        Assert.Null(usuario.NombreCompleto);
        Assert.Null(usuario.UltimoAcceso);
    }
}
```

Create `tests/StockApp.Domain.Tests/Entities/ProductoTests.cs`:

```csharp
using StockApp.Domain.Entities;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class ProductoTests
{
    [Fact]
    public void Producto_Nuevo_TieneStockMinimoEnCero()
    {
        var producto = new Producto
        {
            Codigo = "SKU001",
            Nombre = "Tornillo 6x1",
            UnidadMedidaId = 1,
            PrecioCosto = 10.50m,
            PrecioVenta = 15.00m,
            FechaAlta = DateTime.UtcNow
        };

        Assert.Equal(0m, producto.StockMinimo);
        Assert.Equal(0m, producto.StockActual);
        Assert.True(producto.Activo);
    }

    [Fact]
    public void Producto_CodigoBarras_EsOpcional()
    {
        var producto = new Producto
        {
            Codigo = "SKU002",
            Nombre = "Pintura blanca 1L",
            UnidadMedidaId = 2,
            PrecioCosto = 500m,
            PrecioVenta = 750m,
            FechaAlta = DateTime.UtcNow
        };

        Assert.Null(producto.CodigoBarras);
    }
}
```

Create `tests/StockApp.Domain.Tests/Entities/MovimientoStockTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class MovimientoStockTests
{
    [Fact]
    public void MovimientoStock_Comentario_EsOpcional()
    {
        var movimiento = new MovimientoStock
        {
            ProductoId = 1,
            UsuarioId = 1,
            Tipo = TipoMovimiento.Entrada,
            Cantidad = 10m,
            PrecioUnitario = 100m,
            Fecha = DateTime.UtcNow,
            Motivo = MotivoMovimiento.Compra
        };

        Assert.Null(movimiento.Comentario);
    }

    [Fact]
    public void MovimientoStock_CantidadPositiva_EsValida()
    {
        var movimiento = new MovimientoStock
        {
            ProductoId = 1,
            UsuarioId = 1,
            Tipo = TipoMovimiento.Salida,
            Cantidad = 3.5m,
            PrecioUnitario = 750m,
            Fecha = DateTime.UtcNow,
            Motivo = MotivoMovimiento.Venta
        };

        Assert.True(movimiento.Cantidad > 0);
    }
}
```

- [ ] **Step 2: Verificar que los tests fallan (compilación falla)**

Run: `dotnet test tests/StockApp.Domain.Tests`
Expected: error de compilación (las clases no existen todavía).

### Implementación

- [ ] **Step 3: Crear los enums**

Create `src/StockApp.Domain/Enums/RolUsuario.cs`:

```csharp
namespace StockApp.Domain.Enums;

public enum RolUsuario
{
    Admin,
    Operador
}
```

Create `src/StockApp.Domain/Enums/TipoMovimiento.cs`:

```csharp
namespace StockApp.Domain.Enums;

public enum TipoMovimiento
{
    Entrada,
    Salida
}
```

Create `src/StockApp.Domain/Enums/MotivoMovimiento.cs`:

```csharp
namespace StockApp.Domain.Enums;

public enum MotivoMovimiento
{
    Compra,
    Venta,
    Ajuste,
    Merma
}
```

Create `src/StockApp.Domain/Enums/AccionAuditada.cs`:

```csharp
namespace StockApp.Domain.Enums;

public enum AccionAuditada
{
    CambioPrecio,
    AltaProducto,
    BajaProducto,
    AltaUsuario,
    BajaUsuario,
    CambioRol,
    CambioContrasena
}
```

- [ ] **Step 4: Crear las entidades**

Create `src/StockApp.Domain/Entities/Usuario.cs`:

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

public class Usuario
{
    public int Id { get; set; }
    public string NombreUsuario { get; set; } = string.Empty;   // único, obligatorio
    public string? NombreCompleto { get; set; }
    public string HashContrasena { get; set; } = string.Empty;  // BCrypt; nunca texto plano
    public RolUsuario Rol { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime FechaAlta { get; set; }
    public DateTime? UltimoAcceso { get; set; }
}
```

Create `src/StockApp.Domain/Entities/Producto.cs`:

```csharp
namespace StockApp.Domain.Entities;

public class Producto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;   // SKU interno, único, obligatorio
    public string? CodigoBarras { get; set; }            // EAN opcional; único cuando no es nulo
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int? CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }
    public int? ProveedorId { get; set; }
    public Proveedor? Proveedor { get; set; }
    public int UnidadMedidaId { get; set; }
    public UnidadMedida? UnidadMedida { get; set; }
    public decimal PrecioCosto { get; set; }
    public decimal PrecioVenta { get; set; }
    public decimal StockActual { get; set; }             // saldo denormalizado; ver §6 del spec
    public decimal StockMinimo { get; set; }             // previsto para alertas futuras; default 0
    public bool Activo { get; set; } = true;
    public DateTime FechaAlta { get; set; }
}
```

Create `src/StockApp.Domain/Entities/Categoria.cs`:

```csharp
namespace StockApp.Domain.Entities;

public class Categoria
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;  // obligatorio, único
}
```

Create `src/StockApp.Domain/Entities/Proveedor.cs`:

```csharp
namespace StockApp.Domain.Entities;

public class Proveedor
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public string? Direccion { get; set; }
    public string? Notas { get; set; }
}
```

Create `src/StockApp.Domain/Entities/UnidadMedida.cs`:

```csharp
namespace StockApp.Domain.Entities;

public class UnidadMedida
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;       // ej: Unidad, Metro, Kilo, Litro
    public string Abreviatura { get; set; } = string.Empty;  // ej: u, m, kg, l
}
```

Create `src/StockApp.Domain/Entities/MovimientoStock.cs`:

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

public class MovimientoStock
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public Producto? Producto { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public decimal Cantidad { get; set; }           // siempre positiva; Tipo define el signo
    public decimal PrecioUnitario { get; set; }     // precio del momento (costo o venta)
    public DateTime Fecha { get; set; }
    public MotivoMovimiento Motivo { get; set; }
    public string? Comentario { get; set; }
}
```

Create `src/StockApp.Domain/Entities/LogAuditoria.cs`:

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

public class LogAuditoria
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public DateTime Fecha { get; set; }
    public AccionAuditada Accion { get; set; }
    public string Entidad { get; set; } = string.Empty;   // ej: "Producto", "Usuario"
    public int EntidadId { get; set; }
    public string Detalle { get; set; } = string.Empty;   // ej: "PrecioVenta 100,00 → 120,00"
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Domain.Tests`
Expected: `Passed!  - Failed: 0, Passed: 5`

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Domain tests/StockApp.Domain.Tests
git commit -m "feat(domain): entidades del dominio, enums y tests unitarios"
```

---

## Task 2: AppDbContext con Fluent API

**Files:**
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`

### Tests primero

- [ ] **Step 1: Escribir tests de esquema que fallan**

Create `tests/StockApp.Infrastructure.Tests/Persistence/AppDbContextSchemaTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

public class AppDbContextSchemaTests
{
    private static AppDbContext CrearContextoEnMemoria()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var ctx = new AppDbContext(options);
        ctx.Database.OpenConnection();
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public void AppDbContext_PuedeCrearEsquemaCompleto()
    {
        using var ctx = CrearContextoEnMemoria();
        // Si llegamos acá sin excepción, el esquema es válido
        Assert.NotNull(ctx);
    }

    [Fact]
    public void AppDbContext_Usuarios_ExisteElDbSet()
    {
        using var ctx = CrearContextoEnMemoria();
        Assert.NotNull(ctx.Usuarios);
    }

    [Fact]
    public void AppDbContext_Productos_ExisteElDbSet()
    {
        using var ctx = CrearContextoEnMemoria();
        Assert.NotNull(ctx.Productos);
    }

    [Fact]
    public void AppDbContext_MovimientosStock_ExisteElDbSet()
    {
        using var ctx = CrearContextoEnMemoria();
        Assert.NotNull(ctx.MovimientosStock);
    }

    [Fact]
    public void AppDbContext_LogsAuditoria_ExisteElDbSet()
    {
        using var ctx = CrearContextoEnMemoria();
        Assert.NotNull(ctx.LogsAuditoria);
    }

    [Fact]
    public void AppDbContext_NombreUsuario_TieneIndiceUnico()
    {
        using var ctx = CrearContextoEnMemoria();

        var entityType = ctx.Model.FindEntityType(typeof(StockApp.Domain.Entities.Usuario))!;
        var indices = entityType.GetIndexes();

        Assert.Contains(indices, idx =>
            idx.IsUnique &&
            idx.Properties.Any(p => p.Name == nameof(StockApp.Domain.Entities.Usuario.NombreUsuario)));
    }

    [Fact]
    public void AppDbContext_Codigo_SKU_TieneIndiceUnico()
    {
        using var ctx = CrearContextoEnMemoria();

        var entityType = ctx.Model.FindEntityType(typeof(StockApp.Domain.Entities.Producto))!;
        var indices = entityType.GetIndexes();

        Assert.Contains(indices, idx =>
            idx.IsUnique &&
            idx.Properties.Any(p => p.Name == nameof(StockApp.Domain.Entities.Producto.Codigo)));
    }

    [Fact]
    public void AppDbContext_PuedeInsertar_Y_Recuperar_UnProducto()
    {
        using var ctx = CrearContextoEnMemoria();

        var unidad = new StockApp.Domain.Entities.UnidadMedida
            { Nombre = "Unidad", Abreviatura = "u" };
        ctx.UnidadesMedida.Add(unidad);
        ctx.SaveChanges();

        var producto = new StockApp.Domain.Entities.Producto
        {
            Codigo = "SKU-001",
            Nombre = "Tornillo 6x1",
            UnidadMedidaId = unidad.Id,
            PrecioCosto = 5.00m,
            PrecioVenta = 8.50m,
            FechaAlta = DateTime.UtcNow
        };
        ctx.Productos.Add(producto);
        ctx.SaveChanges();

        var recuperado = ctx.Productos.First(p => p.Codigo == "SKU-001");
        Assert.Equal("Tornillo 6x1", recuperado.Nombre);
        Assert.Equal(8.50m, recuperado.PrecioVenta);
    }
}
```

- [ ] **Step 2: Verificar que los tests fallan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: error de compilación (faltan los `DbSet<>` en `AppDbContext`).

### Implementación

- [ ] **Step 3: Actualizar `AppDbContext` con DbSet y Fluent API**

Edit `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;

namespace StockApp.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<UnidadMedida> UnidadesMedida => Set<UnidadMedida>();
    public DbSet<MovimientoStock> MovimientosStock => Set<MovimientoStock>();
    public DbSet<LogAuditoria> LogsAuditoria => Set<LogAuditoria>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Usuario ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Usuario>(e =>
        {
            e.HasIndex(u => u.NombreUsuario).IsUnique();
            e.Property(u => u.NombreUsuario).IsRequired().HasMaxLength(100);
            e.Property(u => u.HashContrasena).IsRequired();
            e.Property(u => u.Activo).HasDefaultValue(true);
        });

        // ── Producto ──────────────────────────────────────────────────────────
        modelBuilder.Entity<Producto>(e =>
        {
            e.HasIndex(p => p.Codigo).IsUnique();
            e.HasIndex(p => p.CodigoBarras).IsUnique().HasFilter("[CodigoBarras] IS NOT NULL");
            e.Property(p => p.Codigo).IsRequired().HasMaxLength(50);
            e.Property(p => p.Nombre).IsRequired();
            e.Property(p => p.PrecioCosto).HasPrecision(18, 4);
            e.Property(p => p.PrecioVenta).HasPrecision(18, 4);
            e.Property(p => p.StockActual).HasPrecision(18, 4);
            e.Property(p => p.StockMinimo).HasPrecision(18, 4).HasDefaultValue(0m);
            e.Property(p => p.Activo).HasDefaultValue(true);
            e.HasOne(p => p.Categoria).WithMany()
                .HasForeignKey(p => p.CategoriaId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(p => p.Proveedor).WithMany()
                .HasForeignKey(p => p.ProveedorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(p => p.UnidadMedida).WithMany()
                .HasForeignKey(p => p.UnidadMedidaId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Categoria ─────────────────────────────────────────────────────────
        modelBuilder.Entity<Categoria>(e =>
        {
            e.Property(c => c.Nombre).IsRequired();
            e.HasIndex(c => c.Nombre).IsUnique();
        });

        // ── UnidadMedida ──────────────────────────────────────────────────────
        modelBuilder.Entity<UnidadMedida>(e =>
        {
            e.Property(u => u.Nombre).IsRequired();
            e.Property(u => u.Abreviatura).IsRequired().HasMaxLength(10);
        });

        // ── MovimientoStock ───────────────────────────────────────────────────
        modelBuilder.Entity<MovimientoStock>(e =>
        {
            e.Property(m => m.Cantidad).HasPrecision(18, 4);
            e.Property(m => m.PrecioUnitario).HasPrecision(18, 4);
            e.HasOne(m => m.Producto).WithMany()
                .HasForeignKey(m => m.ProductoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Usuario).WithMany()
                .HasForeignKey(m => m.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── LogAuditoria ──────────────────────────────────────────────────────
        modelBuilder.Entity<LogAuditoria>(e =>
        {
            e.Property(l => l.Entidad).IsRequired();
            e.Property(l => l.Detalle).IsRequired();
            e.HasOne(l => l.Usuario).WithMany()
                .HasForeignKey(l => l.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: `Passed!  - Failed: 0, Passed: 8` (el smoke test del Inc 1 + los 7 nuevos).

> Nota: el índice filtrado (`HasFilter("[CodigoBarras] IS NOT NULL")`) es sintaxis
> SQLite/SQL Server. Con SQLite en memoria (`EnsureCreated`) el índice se crea sin filtro;
> en la BD real con migraciones funciona completo.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Infrastructure/Persistence/AppDbContext.cs \
        tests/StockApp.Infrastructure.Tests/Persistence/
git commit -m "feat(infrastructure): AppDbContext con DbSet y Fluent API"
```

---

## Task 3: Abstracción de directorio de datos por plataforma

**Files:**
- Create: `src/StockApp.Infrastructure/Platform/IUserDataPathProvider.cs`
- Create: `src/StockApp.Infrastructure/Platform/UserDataPathProvider.cs`

### Tests primero

- [ ] **Step 1: Escribir tests que fallan**

Create `tests/StockApp.Infrastructure.Tests/Platform/UserDataPathProviderTests.cs`:

```csharp
using StockApp.Infrastructure.Platform;
using Xunit;

namespace StockApp.Infrastructure.Tests.Platform;

public class UserDataPathProviderTests
{
    [Fact]
    public void GetDataDirectory_RetornaRutaQueContiene_StockApp()
    {
        var provider = new UserDataPathProvider();
        var path = provider.GetDataDirectory();

        Assert.Contains("StockApp", path);
    }

    [Fact]
    public void GetDataDirectory_RetornaRutaAbsoluta()
    {
        var provider = new UserDataPathProvider();
        var path = provider.GetDataDirectory();

        Assert.True(Path.IsPathRooted(path));
    }

    [Fact]
    public void GetDatabasePath_TerminaEnArchivoDB()
    {
        var provider = new UserDataPathProvider();
        var dbPath = provider.GetDatabasePath();

        Assert.EndsWith(".db", dbPath);
    }
}
```

- [ ] **Step 2: Verificar que los tests fallan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: error de compilación (la clase no existe todavía).

### Implementación

- [ ] **Step 3: Crear la interfaz y la implementación**

Create `src/StockApp.Infrastructure/Platform/IUserDataPathProvider.cs`:

```csharp
namespace StockApp.Infrastructure.Platform;

public interface IUserDataPathProvider
{
    /// <summary>
    /// Retorna el directorio de datos del usuario para StockApp.
    /// Windows: %LOCALAPPDATA%\StockApp\
    /// Linux:   ~/.local/share/StockApp/
    /// </summary>
    string GetDataDirectory();

    /// <summary>Ruta completa al archivo .db de la base de datos.</summary>
    string GetDatabasePath();

    /// <summary>Ruta completa al subdirectorio de backups.</summary>
    string GetBackupsDirectory();
}
```

Create `src/StockApp.Infrastructure/Platform/UserDataPathProvider.cs`:

```csharp
namespace StockApp.Infrastructure.Platform;

public class UserDataPathProvider : IUserDataPathProvider
{
    private const string AppName = "StockApp";
    private const string DbFileName = "stockapp.db";
    private const string BackupsSubdir = "backups";

    public string GetDataDirectory()
    {
        var baseDir = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(baseDir, AppName);
    }

    public string GetDatabasePath()
        => Path.Combine(GetDataDirectory(), DbFileName);

    public string GetBackupsDirectory()
        => Path.Combine(GetDataDirectory(), BackupsSubdir);
}
```

- [ ] **Step 4: Correr los tests**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: `Passed!` — sin regresiones.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Infrastructure/Platform \
        tests/StockApp.Infrastructure.Tests/Platform/
git commit -m "feat(infrastructure): abstracción de directorio de datos por plataforma"
```

---

## Task 4: Servicio de backup

**Files:**
- Create: `src/StockApp.Infrastructure/Services/BackupService.cs`

### Tests primero

- [ ] **Step 1: Escribir tests que fallan**

Create `tests/StockApp.Infrastructure.Tests/Services/BackupServiceTests.cs`:

```csharp
using StockApp.Infrastructure.Services;
using Xunit;

namespace StockApp.Infrastructure.Tests.Services;

public class BackupServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _backupsDir;

    public BackupServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "stockapp.db");
        _backupsDir = Path.Combine(_tempDir, "backups");
        // Crear un .db de prueba
        File.WriteAllText(_dbPath, "SQLite dummy content");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task CrearBackup_CreaArchivoConTimestamp()
    {
        var service = new BackupService(_dbPath, _backupsDir);

        await service.CrearBackupAsync("pre-migration");

        var archivos = Directory.GetFiles(_backupsDir, "*.db");
        Assert.Single(archivos);
        Assert.Contains("pre-migration", archivos[0]);
    }

    [Fact]
    public async Task CrearBackup_ContenidoEsIdentico()
    {
        var service = new BackupService(_dbPath, _backupsDir);

        await service.CrearBackupAsync("test");

        var backup = Directory.GetFiles(_backupsDir, "*.db").First();
        var contenido = await File.ReadAllTextAsync(backup);
        Assert.Equal("SQLite dummy content", contenido);
    }

    [Fact]
    public async Task AplicarRetencion_EliminaBackupsMasViejosDe7Dias()
    {
        Directory.CreateDirectory(_backupsDir);
        var service = new BackupService(_dbPath, _backupsDir);

        // Crear 3 backups viejos y 1 reciente
        for (int i = 8; i <= 10; i++)
        {
            var archivo = Path.Combine(_backupsDir, $"backup-{i}days.db");
            File.WriteAllText(archivo, "old");
            File.SetLastWriteTimeUtc(archivo, DateTime.UtcNow.AddDays(-i));
        }
        var reciente = Path.Combine(_backupsDir, "backup-reciente.db");
        File.WriteAllText(reciente, "recent");

        await service.AplicarRetencionAsync();

        var restantes = Directory.GetFiles(_backupsDir, "*.db");
        Assert.Single(restantes); // solo el reciente
        Assert.Contains("reciente", restantes[0]);
    }

    [Fact]
    public async Task AplicarRetencion_ConservaSiempreElMasReciente_AunqueTengaMasDe7Dias()
    {
        Directory.CreateDirectory(_backupsDir);
        var service = new BackupService(_dbPath, _backupsDir);

        // Todos los backups son viejos — el más reciente tiene 15 días
        var backups = new[] { 30, 20, 15 };
        foreach (var dias in backups)
        {
            var archivo = Path.Combine(_backupsDir, $"backup-{dias}dias.db");
            File.WriteAllText(archivo, "old");
            File.SetLastWriteTimeUtc(archivo, DateTime.UtcNow.AddDays(-dias));
        }

        await service.AplicarRetencionAsync();

        var restantes = Directory.GetFiles(_backupsDir, "*.db");
        Assert.Single(restantes); // conserva el de 15 días (el más reciente de todos)
        Assert.Contains("15dias", restantes[0]);
    }

    [Fact]
    public async Task DebeHacerBackup_RetornaTrue_SiPasaronMas12Horas()
    {
        var timestampFile = Path.Combine(_tempDir, "last-backup.txt");
        await File.WriteAllTextAsync(timestampFile,
            DateTime.UtcNow.AddHours(-13).ToString("O"));

        var service = new BackupService(_dbPath, _backupsDir, timestampFile);

        Assert.True(await service.DebeHacerBackupPeriodicoAsync());
    }

    [Fact]
    public async Task DebeHacerBackup_RetornaFalse_SiNoHanPasado12Horas()
    {
        var timestampFile = Path.Combine(_tempDir, "last-backup.txt");
        await File.WriteAllTextAsync(timestampFile,
            DateTime.UtcNow.AddHours(-2).ToString("O"));

        var service = new BackupService(_dbPath, _backupsDir, timestampFile);

        Assert.False(await service.DebeHacerBackupPeriodicoAsync());
    }

    [Fact]
    public async Task DebeHacerBackup_RetornaTrue_SiNoExisteTimestamp()
    {
        var timestampFile = Path.Combine(_tempDir, "no-existe.txt");
        var service = new BackupService(_dbPath, _backupsDir, timestampFile);

        Assert.True(await service.DebeHacerBackupPeriodicoAsync());
    }
}
```

- [ ] **Step 2: Verificar que los tests fallan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: error de compilación (`BackupService` no existe todavía).

### Implementación

- [ ] **Step 3: Crear el servicio de backup**

Create `src/StockApp.Infrastructure/Services/BackupService.cs`:

```csharp
namespace StockApp.Infrastructure.Services;

public class BackupService
{
    private const int RetencionDias = 7;
    private const int IntervalHoras = 12;

    private readonly string _dbPath;
    private readonly string _backupsDir;
    private readonly string _timestampFile;

    public BackupService(string dbPath, string backupsDir, string? timestampFile = null)
    {
        _dbPath = dbPath;
        _backupsDir = backupsDir;
        _timestampFile = timestampFile
            ?? Path.Combine(Path.GetDirectoryName(backupsDir)!, "last-backup.txt");
    }

    /// <summary>
    /// Crea un backup del .db con timestamp y etiqueta (p. ej. "pre-migration", "periodic").
    /// No lanza excepciones: si falla, loguea y sigue.
    /// </summary>
    public async Task CrearBackupAsync(string etiqueta)
    {
        try
        {
            Directory.CreateDirectory(_backupsDir);
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var destino = Path.Combine(_backupsDir, $"backup-{timestamp}-{etiqueta}.db");
            await Task.Run(() => File.Copy(_dbPath, destino, overwrite: false));
            await PersistirTimestampAsync();
        }
        catch (Exception ex)
        {
            // Salvaguarda 2: fallo de backup se loguea pero no rompe la app
            Console.Error.WriteLine($"[BackupService] Error al crear backup ({etiqueta}): {ex.Message}");
        }
    }

    /// <summary>
    /// Elimina backups de más de 7 días, pero siempre conserva al menos el más reciente.
    /// </summary>
    public async Task AplicarRetencionAsync()
    {
        await Task.Run(() =>
        {
            if (!Directory.Exists(_backupsDir)) return;

            var archivos = Directory.GetFiles(_backupsDir, "*.db")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            if (archivos.Count == 0) return;

            var limite = DateTime.UtcNow.AddDays(-RetencionDias);

            // El más reciente siempre se conserva (índice 0)
            foreach (var archivo in archivos.Skip(1))
            {
                if (archivo.LastWriteTimeUtc < limite)
                {
                    try { archivo.Delete(); }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"[BackupService] No se pudo borrar {archivo.Name}: {ex.Message}");
                    }
                }
            }
        });
    }

    /// <summary>
    /// Retorna true si pasaron ≥12 h desde el último backup o si no hay registro.
    /// </summary>
    public async Task<bool> DebeHacerBackupPeriodicoAsync()
    {
        if (!File.Exists(_timestampFile)) return true;

        try
        {
            var texto = await File.ReadAllTextAsync(_timestampFile);
            if (DateTime.TryParse(texto, null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var ultimo))
            {
                return (DateTime.UtcNow - ultimo).TotalHours >= IntervalHoras;
            }
        }
        catch { /* archivo corrupto → tratar como "no existe" */ }

        return true;
    }

    private async Task PersistirTimestampAsync()
    {
        try
        {
            await File.WriteAllTextAsync(_timestampFile, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[BackupService] No se pudo persistir timestamp: {ex.Message}");
        }
    }
}
```

- [ ] **Step 4: Correr los tests**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: `Passed!` — todos los tests de `BackupServiceTests` verdes.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Infrastructure/Services/BackupService.cs \
        tests/StockApp.Infrastructure.Tests/Services/BackupServiceTests.cs
git commit -m "feat(infrastructure): servicio de backup con retención y trigger por timestamp"
```

---

## Task 5: DatabaseInitializer — migración con backup pre-migración

**Files:**
- Create: `src/StockApp.Infrastructure/Services/DatabaseInitializer.cs`

### Tests primero

- [ ] **Step 1: Escribir tests que fallan**

Create `tests/StockApp.Infrastructure.Tests/Services/DatabaseInitializerTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Services;
using Xunit;

namespace StockApp.Infrastructure.Tests.Services;

public class DatabaseInitializerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _backupsDir;

    public DatabaseInitializerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _backupsDir = Path.Combine(_tempDir, "backups");
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"DataSource={_dbPath}")
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task InicializarAsync_CreaLaBD_SiNoExiste()
    {
        using var ctx = CrearContexto();
        var backup = new BackupService(_dbPath, _backupsDir);
        var initializer = new DatabaseInitializer(ctx, backup);

        await initializer.InicializarAsync();

        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public async Task InicializarAsync_CreaBackup_SiLaBDYaExiste()
    {
        // Pre-condición: BD ya existente (simula app ya instalada)
        File.WriteAllText(_dbPath, "existing db");

        using var ctx = CrearContexto();
        var backup = new BackupService(_dbPath, _backupsDir);
        var initializer = new DatabaseInitializer(ctx, backup);

        await initializer.InicializarAsync();

        var backups = Directory.GetFiles(_backupsDir, "*.db");
        Assert.NotEmpty(backups);
        Assert.Contains("pre-migration", backups[0]);
    }

    [Fact]
    public async Task InicializarAsync_NoFalla_SiNoBdPrevia()
    {
        // Primera instalación: no hay BD, no debería intentar backup
        using var ctx = CrearContexto();
        var backup = new BackupService(_dbPath, _backupsDir);
        var initializer = new DatabaseInitializer(ctx, backup);

        var ex = await Record.ExceptionAsync(() => initializer.InicializarAsync());

        Assert.Null(ex);
    }
}
```

- [ ] **Step 2: Verificar que los tests fallan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: error de compilación (`DatabaseInitializer` no existe todavía).

### Implementación

- [ ] **Step 3: Crear el DatabaseInitializer**

Create `src/StockApp.Infrastructure/Services/DatabaseInitializer.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Services;

public class DatabaseInitializer
{
    private readonly AppDbContext _context;
    private readonly BackupService _backupService;

    public DatabaseInitializer(AppDbContext context, BackupService backupService)
    {
        _context = context;
        _backupService = backupService;
    }

    /// <summary>
    /// Orquesta el arranque de la base de datos:
    /// 1. Si el .db ya existe → backup pre-migración.
    /// 2. Aplica migraciones pendientes (Database.Migrate()).
    /// 3. Aplica retención de backups.
    /// </summary>
    public async Task InicializarAsync()
    {
        var dbPath = _context.Database.GetDbConnection().DataSource;
        var dbExiste = !string.IsNullOrEmpty(dbPath) && File.Exists(dbPath);

        if (dbExiste)
        {
            await _backupService.CrearBackupAsync("pre-migration");
        }

        await _context.Database.MigrateAsync();

        await _backupService.AplicarRetencionAsync();
    }
}
```

- [ ] **Step 4: Instalar paquete de Design para herramientas EF en Infrastructure**

> Ya instalado en el Inc 1. Verificar que está en el `.csproj`:
> `Microsoft.EntityFrameworkCore.Design` debe estar referenciado.

- [ ] **Step 5: Correr los tests**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: `Passed!` — todos verdes.

> Nota: los tests de `DatabaseInitializer` usan un archivo `.db` temporal real (no en
> memoria) porque `Database.Migrate()` necesita un proveedor con soporte de migraciones.
> Con `:memory:` y `EnsureCreated()` no se generan ni aplican migraciones.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Infrastructure/Services/DatabaseInitializer.cs \
        tests/StockApp.Infrastructure.Tests/Services/DatabaseInitializerTests.cs
git commit -m "feat(infrastructure): DatabaseInitializer con backup pre-migración y retención"
```

---

## Task 6: Primera migración EF Core (`InitialCreate`)

**Files:**
- Create: `src/StockApp.Infrastructure/Migrations/` (generado por herramienta)

- [ ] **Step 1: Generar la migración con `dotnet ef`**

```bash
dotnet ef migrations add InitialCreate \
  --project src/StockApp.Infrastructure \
  --startup-project src/StockApp.Presentation
```

Expected: crea `src/StockApp.Infrastructure/Migrations/` con dos archivos:
- `<timestamp>_InitialCreate.cs` — las operaciones `Up` (create tables) y `Down` (drop tables).
- `AppDbContextModelSnapshot.cs` — snapshot del modelo.

- [ ] **Step 2: Revisar la migración generada**

Verificar que `Up()` contiene `CreateTable` para todas las entidades:
`Usuarios`, `Productos`, `Categorias`, `Proveedores`, `UnidadesMedida`,
`MovimientosStock`, `LogsAuditoria`. Verificar los índices únicos.

- [ ] **Step 3: Verificar que los tests siguen en verde**

Run: `dotnet test`
Expected: `Passed!` — sin regresiones.

- [ ] **Step 4: Commit**

```bash
git add src/StockApp.Infrastructure/Migrations
git commit -m "chore(infrastructure): primera migración EF Core (InitialCreate)"
```

---

## Task 7: Inyección de dependencias en Presentation

**Files:**
- Modify: `src/StockApp.Presentation/App.axaml.cs`

- [ ] **Step 1: Agregar paquetes necesarios**

```bash
dotnet add src/StockApp.Presentation/StockApp.Presentation.csproj \
  package Microsoft.Extensions.DependencyInjection
```

- [ ] **Step 2: Cablear DI en el arranque de Avalonia**

Edit `src/StockApp.Presentation/App.axaml.cs` para registrar:
- `AppDbContext` (con `UseSqlite` apuntando a la ruta del `IUserDataPathProvider`)
- `IUserDataPathProvider` → `UserDataPathProvider`
- `BackupService` (como singleton o transient, según patrón Avalonia elegido)
- `DatabaseInitializer`

```csharp
// Fragmento representativo del registro en OnFrameworkInitializationCompleted()
services.AddSingleton<IUserDataPathProvider, UserDataPathProvider>();
services.AddDbContext<AppDbContext>((sp, options) =>
{
    var pathProvider = sp.GetRequiredService<IUserDataPathProvider>();
    var dataDir = pathProvider.GetDataDirectory();
    Directory.CreateDirectory(dataDir);
    options.UseSqlite($"DataSource={pathProvider.GetDatabasePath()}");
});
services.AddTransient<BackupService>(sp =>
{
    var pathProvider = sp.GetRequiredService<IUserDataPathProvider>();
    return new BackupService(
        pathProvider.GetDatabasePath(),
        pathProvider.GetBackupsDirectory());
});
services.AddTransient<DatabaseInitializer>();
```

- [ ] **Step 3: Llamar a `DatabaseInitializer.InicializarAsync()` al arrancar**

En el punto de inicio de la app (antes de mostrar la ventana principal), resolver
`DatabaseInitializer` del contenedor y llamar a `InicializarAsync()`. Esto garantiza que
la BD esté lista y con backup antes de que el usuario interactúe con la app.

- [ ] **Step 4: Verificar que la solución compila**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Correr toda la suite de tests**

Run: `dotnet test`
Expected: `Passed!` — todos los tests verdes, sin regresiones.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/App.axaml.cs
git commit -m "feat(presentation): inyección de dependencias y DatabaseInitializer al arrancar"
```

---

## Task 8: Backup periódico con timer híbrido

**Files:**
- Create: `src/StockApp.Infrastructure/Services/BackupPeriodicoService.cs`

### Tests primero

Los tests de lógica de "¿debe hacer backup?" ya están cubiertos en `BackupServiceTests`
(Task 4, Step 1). Acá testeamos que el servicio periódico orquesta correctamente.

- [ ] **Step 1: Escribir tests de orquestación que fallan**

Agregar en `tests/StockApp.Infrastructure.Tests/Services/BackupServiceTests.cs`:

```csharp
[Fact]
public async Task BackupPeriodico_HaceBackup_SiDeberia()
{
    // timestamp viejo → debe hacer backup
    var timestampFile = Path.Combine(_tempDir, "ts.txt");
    await File.WriteAllTextAsync(timestampFile,
        DateTime.UtcNow.AddHours(-13).ToString("O"));

    var service = new BackupService(_dbPath, _backupsDir, timestampFile);

    await service.EjecutarBackupPeriodicoSiCorrespondeAsync();

    var backups = Directory.GetFiles(_backupsDir, "*.db");
    Assert.Single(backups);
    Assert.Contains("periodic", backups[0]);
}

[Fact]
public async Task BackupPeriodico_NoHaceBackup_SiNoDeberia()
{
    var timestampFile = Path.Combine(_tempDir, "ts.txt");
    await File.WriteAllTextAsync(timestampFile,
        DateTime.UtcNow.AddHours(-2).ToString("O"));

    var service = new BackupService(_dbPath, _backupsDir, timestampFile);

    await service.EjecutarBackupPeriodicoSiCorrespondeAsync();

    var backups = Directory.Exists(_backupsDir)
        ? Directory.GetFiles(_backupsDir, "*.db")
        : Array.Empty<string>();
    Assert.Empty(backups);
}
```

- [ ] **Step 2: Agregar el método `EjecutarBackupPeriodicoSiCorrespondeAsync` al `BackupService`**

Edit `src/StockApp.Infrastructure/Services/BackupService.cs`:

```csharp
/// <summary>
/// Compara el timestamp persistido; si pasaron ≥12 h, hace backup y actualiza el timestamp.
/// Llamar al arrancar y desde el timer.
/// </summary>
public async Task EjecutarBackupPeriodicoSiCorrespondeAsync()
{
    if (await DebeHacerBackupPeriodicoAsync())
    {
        await CrearBackupAsync("periodic");
        await AplicarRetencionAsync();
    }
}
```

- [ ] **Step 3: Crear el servicio de timer periódico**

Create `src/StockApp.Infrastructure/Services/BackupPeriodicoService.cs`:

```csharp
namespace StockApp.Infrastructure.Services;

/// <summary>
/// Mantiene un timer que dispara <see cref="BackupService.EjecutarBackupPeriodicoSiCorrespondeAsync"/>
/// cada 12 h mientras la app está corriendo.
/// Se registra como singleton y se arranca desde el punto de composición (App.axaml.cs).
/// </summary>
public sealed class BackupPeriodicoService : IDisposable
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(12);

    private readonly BackupService _backupService;
    private Timer? _timer;

    public BackupPeriodicoService(BackupService backupService)
        => _backupService = backupService;

    /// <summary>
    /// Arranca el timer. También ejecuta el chequeo inicial al arrancar.
    /// </summary>
    public async Task IniciarAsync()
    {
        // Chequeo al arrancar (trigger híbrido: cubre el caso "no abrió la app en 12 h")
        await _backupService.EjecutarBackupPeriodicoSiCorrespondeAsync();

        // Timer que dispara cada 12 h mientras la app corre
        _timer = new Timer(
            callback: _ => _ = _backupService.EjecutarBackupPeriodicoSiCorrespondeAsync(),
            state: null,
            dueTime: Intervalo,
            period: Intervalo);
    }

    public void Dispose() => _timer?.Dispose();
}
```

- [ ] **Step 4: Registrar `BackupPeriodicoService` en DI y arrancarlo**

En `App.axaml.cs`, registrar como singleton y llamar a `IniciarAsync()` en el arranque,
después de `DatabaseInitializer.InicializarAsync()`.

- [ ] **Step 5: Correr toda la suite de tests**

Run: `dotnet test`
Expected: `Passed!` — sin regresiones.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Infrastructure/Services/BackupService.cs \
        src/StockApp.Infrastructure/Services/BackupPeriodicoService.cs \
        tests/StockApp.Infrastructure.Tests/Services/BackupServiceTests.cs
git commit -m "feat(infrastructure): backup periódico con timer híbrido (arranque + timer 12h)"
```

---

## Self-Review (cobertura del incremento)

- ✅ Todas las entidades del dominio (§4) con sus enums creadas y testeadas (Task 1).
- ✅ `AppDbContext` con `DbSet<>`, Fluent API, índices únicos y precisión decimal (Task 2).
- ✅ `IUserDataPathProvider` abstrae `%LOCALAPPDATA%\StockApp\` / `~/.local/share/StockApp/` (Task 3).
- ✅ `BackupService` crea backup con timestamp, aplica retención 7 días y conserva mínimo 1 (Task 4).
- ✅ `DatabaseInitializer` ejecuta backup pre-migración → `Database.Migrate()` → retención (Task 5).
- ✅ Primera migración `InitialCreate` generada con `dotnet ef`, esquema completo verificado (Task 6).
- ✅ DI cableado en `App.axaml.cs`: `AppDbContext`, `UserDataPathProvider`, `DatabaseInitializer` (Task 7).
- ✅ `BackupPeriodicoService` con timer 12 h + chequeo al arrancar (Task 8).

---

## Definition of Done

El Incremento 2 se considera completo cuando:

1. `dotnet test` pasa con **0 failures** en los cuatro proyectos de test.
2. La migración `InitialCreate` se aplica limpiamente sobre una BD nueva: `dotnet ef database update` termina sin error.
3. Al arrancar la app, la BD se crea en `%LOCALAPPDATA%\StockApp\stockapp.db` (Windows) o `~/.local/share/StockApp/stockapp.db` (Linux).
4. En el segundo arranque (BD ya existente), se crea al menos un archivo en el directorio `backups/` con el prefijo `backup-` y la etiqueta `pre-migration`.
5. La retención elimina backups de más de 7 días y conserva al menos el más reciente (verificado por test).
6. Ningún fallo de backup interrumpe el arranque de la app (verificado por test con directorio no escribible o similar).

**Lo que este incremento NO hace (próximos incrementos):** autenticación, login, gestión de usuarios, catálogo, movimientos ni cualquier feature de UI. Eso arranca en el Incremento 3 (Auth + Usuarios + Autorización).
