using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;
using Xunit;

namespace StockApp.Infrastructure.Tests.Migrations;

/// <summary>
/// Verifica que AddCatalogoExtensions aplica correctamente sobre SQLite real.
/// Usa archivo temporal + MigrateAsync (NO EnsureCreated, que saltea migraciones).
/// </summary>
public class AddCatalogoExtensionsMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public AddCatalogoExtensionsMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"stockapp_catalogo_{Path.GetRandomFileName()}.db");
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    private AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"DataSource={_dbPath}")
            .Options;
        return new AppDbContext(options);
    }

    // ── 1. La migración se aplica sin error ───────────────────────────────────

    [Fact]
    public async Task MigrateAsync_AplicaAddCatalogoExtensions_SinError()
    {
        using var ctx = CrearContexto();

        await ctx.Database.MigrateAsync();

        Assert.True(File.Exists(_dbPath));
    }

    // ── 2. Columna Activo en Categorias — default true en filas preexistentes ─

    [Fact]
    public async Task Categorias_Activo_DefaultTrue_EnFilasInsertadas()
    {
        using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();

        ctx.Categorias.Add(new Categoria { Nombre = "TestCategoria" });
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var cat = await ctx.Categorias.SingleAsync(c => c.Nombre == "TestCategoria");
        Assert.True(cat.Activo);
    }

    // ── 3. Columna Activo en Proveedores — default true ───────────────────────

    [Fact]
    public async Task Proveedores_Activo_DefaultTrue_EnFilasInsertadas()
    {
        using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();

        ctx.Proveedores.Add(new Proveedor { Nombre = "TestProveedor" });
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var prov = await ctx.Proveedores.SingleAsync(p => p.Nombre == "TestProveedor");
        Assert.True(prov.Activo);
    }

    // ── 4. Columna Activo en UnidadesMedida — default true ───────────────────

    [Fact]
    public async Task UnidadesMedida_Activo_DefaultTrue_EnFilasInsertadas()
    {
        using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();

        ctx.UnidadesMedida.Add(new UnidadMedida { Nombre = "TestUnidad", Abreviatura = "tu" });
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var unidad = await ctx.UnidadesMedida.SingleAsync(u => u.Nombre == "TestUnidad");
        Assert.True(unidad.Activo);
    }

    // ── 5. Índice único en Proveedor.Nombre ───────────────────────────────────

    [Fact]
    public async Task Proveedor_NombreDuplicado_LanzaDbUpdateException()
    {
        using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();

        ctx.Proveedores.Add(new Proveedor { Nombre = "DistribuidoraUnica" });
        await ctx.SaveChangesAsync();

        using var ctx2 = CrearContexto();
        ctx2.Proveedores.Add(new Proveedor { Nombre = "DistribuidoraUnica" });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    // ── 6. Índice único en UnidadMedida.Nombre ────────────────────────────────

    [Fact]
    public async Task UnidadMedida_NombreDuplicado_LanzaDbUpdateException()
    {
        using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();

        ctx.UnidadesMedida.Add(new UnidadMedida { Nombre = "Kilogramo", Abreviatura = "kg" });
        await ctx.SaveChangesAsync();

        using var ctx2 = CrearContexto();
        ctx2.UnidadesMedida.Add(new UnidadMedida { Nombre = "Kilogramo", Abreviatura = "kg2" });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    // ── 7. Índice único en UnidadMedida.Abreviatura ───────────────────────────

    [Fact]
    public async Task UnidadMedida_AbrebiaturaDuplicada_LanzaDbUpdateException()
    {
        using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();

        ctx.UnidadesMedida.Add(new UnidadMedida { Nombre = "Litro", Abreviatura = "l" });
        await ctx.SaveChangesAsync();

        using var ctx2 = CrearContexto();
        ctx2.UnidadesMedida.Add(new UnidadMedida { Nombre = "Litro2", Abreviatura = "l" });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx2.SaveChangesAsync());
    }

    // ── 8. Up / Down / Up no rompe el esquema ─────────────────────────────────

    [Fact]
    public async Task UpDownUp_SinErrorDeEsquema()
    {
        using var ctx = CrearContexto();

        // Aplica todas las migraciones (Up)
        await ctx.Database.MigrateAsync();

        // Retrocede hasta InitialCreate (antes de AddCatalogoExtensions)
        await ctx.Database.MigrateAsync("InitialCreate");

        // Vuelve a aplicar hasta el último (Up de nuevo)
        await ctx.Database.MigrateAsync();

        // Si llegó hasta acá sin excepción, la migración es idempotente en Down/Up
        Assert.True(true);
    }
}
