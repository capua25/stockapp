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
        // Pre-condición: BD ya existente con migraciones aplicadas (simula app ya instalada).
        // Usamos MigrateAsync (no EnsureCreated) para que __EFMigrationsHistory exista y
        // el segundo MigrateAsync encuentre todo al día (no intenta re-aplicar nada).
        {
            using var ctxPrevio = CrearContexto();
            await ctxPrevio.Database.MigrateAsync();
        }

        // En este punto el .db existe → el initializer debe hacer backup pre-migración
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
        // Primera instalación: no hay BD — no debería intentar backup
        using var ctx = CrearContexto();
        var backup = new BackupService(_dbPath, _backupsDir);
        var initializer = new DatabaseInitializer(ctx, backup);

        var ex = await Record.ExceptionAsync(() => initializer.InicializarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task InicializarAsync_NoHaceBackup_EnPrimerArranque()
    {
        // Sin BD previa no debe crearse ningún backup
        using var ctx = CrearContexto();
        var backup = new BackupService(_dbPath, _backupsDir);
        var initializer = new DatabaseInitializer(ctx, backup);

        await initializer.InicializarAsync();

        var backups = Directory.Exists(_backupsDir)
            ? Directory.GetFiles(_backupsDir, "*.db")
            : Array.Empty<string>();
        Assert.Empty(backups);
    }
}
