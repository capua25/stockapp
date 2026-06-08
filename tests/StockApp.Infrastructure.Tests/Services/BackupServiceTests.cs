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
        Assert.Single(restantes);
        Assert.Contains("reciente", restantes[0]);
    }

    [Fact]
    public async Task AplicarRetencion_ConservaSiempreElMasReciente_AunqueTengaMasDe7Dias()
    {
        Directory.CreateDirectory(_backupsDir);
        var service = new BackupService(_dbPath, _backupsDir);

        var backups = new[] { 30, 20, 15 };
        foreach (var dias in backups)
        {
            var archivo = Path.Combine(_backupsDir, $"backup-{dias}dias.db");
            File.WriteAllText(archivo, "old");
            File.SetLastWriteTimeUtc(archivo, DateTime.UtcNow.AddDays(-dias));
        }

        await service.AplicarRetencionAsync();

        var restantes = Directory.GetFiles(_backupsDir, "*.db");
        Assert.Single(restantes);
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

    [Fact]
    public async Task CrearBackup_ConRutaInvalida_NoLanzaExcepcion()
    {
        // Ruta imposible (doble nulo en nombre) → falla de disco/permisos
        var rutaInvalida = Path.Combine(_tempDir, "stockapp-invalid.db");
        // No creamos el archivo → File.Copy fallará, pero el servicio no debe lanzar
        var service = new BackupService(rutaInvalida, _backupsDir);

        var ex = await Record.ExceptionAsync(() => service.CrearBackupAsync("fail-test"));

        Assert.Null(ex);
    }

    // ── Tests de backup periódico (Task 8) ──────────────────────────────────

    [Fact]
    public async Task BackupPeriodico_HaceBackup_SiDeberia()
    {
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
}
