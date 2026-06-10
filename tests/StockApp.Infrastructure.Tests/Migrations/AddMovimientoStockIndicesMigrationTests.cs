using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;
using Xunit;

namespace StockApp.Infrastructure.Tests.Migrations;

/// <summary>
/// Verifica que AddMovimientoStockIndices aplica el índice compuesto
/// IX_MovimientosStock_ProductoId_Fecha sobre SQLite real (archivo temporal + MigrateAsync).
/// </summary>
public class AddMovimientoStockIndicesMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public AddMovimientoStockIndicesMigrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"stockapp_movindices_{Path.GetRandomFileName()}.db");
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
    public async Task MigrateAsync_AplicaAddMovimientoStockIndices_SinError()
    {
        using var ctx = CrearContexto();

        await ctx.Database.MigrateAsync();

        Assert.True(File.Exists(_dbPath));
    }

    // ── 2. El índice IX_MovimientosStock_ProductoId_Fecha existe en sqlite_master ─

    [Fact]
    public async Task Indice_IX_MovimientosStock_ProductoId_Fecha_Existe()
    {
        using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();

        // Consultar sqlite_master para verificar que el índice fue creado
        using var conn = new SqliteConnection($"DataSource={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='index' AND name='IX_MovimientosStock_ProductoId_Fecha'";
        var count = (long)cmd.ExecuteScalar()!;

        Assert.Equal(1L, count);
    }

    // ── 3. Up / Down / Up no rompe el esquema ────────────────────────────────

    [Fact]
    public async Task UpDownUp_SinErrorDeEsquema()
    {
        using var ctx = CrearContexto();

        // Aplica todas las migraciones
        await ctx.Database.MigrateAsync();

        // Retrocede hasta AddCatalogoExtensions (antes de AddMovimientoStockIndices)
        await ctx.Database.MigrateAsync("20260609211956_AddCatalogoExtensions");

        // Vuelve a aplicar hasta el último
        await ctx.Database.MigrateAsync();

        Assert.True(true);
    }
}
