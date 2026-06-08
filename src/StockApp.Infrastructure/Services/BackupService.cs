namespace StockApp.Infrastructure.Services;

public class BackupService
{
    private const int RetencionDias = 7;
    private const int IntervaloHoras = 12;

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
    /// Crea una copia del .db con timestamp y etiqueta en la carpeta de backups.
    /// Si falla (disco lleno, permisos, archivo origen ausente), loguea y NO lanza.
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
            Console.Error.WriteLine($"[BackupService] Error al crear backup ({etiqueta}): {ex.Message}");
        }
    }

    /// <summary>
    /// Elimina backups de más de 7 días. Siempre conserva el más reciente, incluso si
    /// supera el umbral de retención (salvaguarda: no dejar al usuario sin ningún backup).
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

            // El índice 0 es el más reciente — siempre se conserva
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
    /// Retorna true si pasaron ≥12 h desde el último backup registrado, o si no hay registro.
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
                return (DateTime.UtcNow - ultimo).TotalHours >= IntervaloHoras;
            }
        }
        catch
        {
            // Archivo corrupto → tratar como si no existiera
        }

        return true;
    }

    /// <summary>
    /// Si corresponde (≥12 h desde el último), hace el backup periódico y aplica retención.
    /// </summary>
    public async Task EjecutarBackupPeriodicoSiCorrespondeAsync()
    {
        if (await DebeHacerBackupPeriodicoAsync())
        {
            await CrearBackupAsync("periodic");
            await AplicarRetencionAsync();
        }
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
