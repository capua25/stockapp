using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;
using Xunit;

namespace StockApp.Infrastructure.Tests.Migrations;

/// <summary>
/// Verifica que la migración InitialCreate crea el esquema completo sobre una BD vacía.
/// Usa archivo temporal (no in-memory) porque Database.Migrate() requiere proveedor real.
/// </summary>
public class InitialCreateMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public InitialCreateMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"stockapp_test_{Path.GetRandomFileName()}.db");
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

    [Fact]
    public async Task Migrate_CreaEsquemaCompleto_SobreBdVacia()
    {
        using var ctx = CrearContexto();

        await ctx.Database.MigrateAsync();

        Assert.True(File.Exists(_dbPath));
    }

    [Fact]
    public async Task Migrate_PermiteInsertar_Usuario()
    {
        using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();

        ctx.Usuarios.Add(new StockApp.Domain.Entities.Usuario
        {
            NombreUsuario = "admin",
            HashContrasena = "hash",
            Rol = StockApp.Domain.Enums.RolUsuario.Admin,
            FechaAlta = DateTime.UtcNow
        });
        var rows = await ctx.SaveChangesAsync();

        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Migrate_PermiteInsertar_Producto_ConPrecisionDecimal()
    {
        using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();

        var unidad = new StockApp.Domain.Entities.UnidadMedida
            { Nombre = "Unidad", Abreviatura = "u" };
        ctx.UnidadesMedida.Add(unidad);
        await ctx.SaveChangesAsync();

        ctx.Productos.Add(new StockApp.Domain.Entities.Producto
        {
            Codigo = "SKU-M01",
            Nombre = "Tornillo",
            UnidadMedidaId = unidad.Id,
            PrecioCosto = 1234.5678m,
            PrecioVenta = 9876.1234m,
            FechaAlta = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        ctx.ChangeTracker.Clear();
        var leido = await ctx.Productos.SingleAsync(p => p.Codigo == "SKU-M01");
        Assert.Equal(1234.5678m, leido.PrecioCosto);
        Assert.Equal(9876.1234m, leido.PrecioVenta);
    }
}
