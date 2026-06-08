namespace StockApp.Infrastructure.Services;

/// <summary>
/// Mantiene un timer que dispara <see cref="BackupService.EjecutarBackupPeriodicoSiCorrespondeAsync"/>
/// cada 12 h mientras la app está corriendo (trigger periódico). Al arrancar también evalúa
/// si corresponde hacer backup (trigger híbrido: cubre el caso "app no abrió en 12 h").
/// Registrar como singleton en DI y llamar a IniciarAsync() desde el punto de composición.
/// </summary>
public sealed class BackupPeriodicoService : IDisposable
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromHours(12);

    private readonly BackupService _backupService;
    private Timer? _timer;

    public BackupPeriodicoService(BackupService backupService)
        => _backupService = backupService;

    /// <summary>
    /// Evalúa el backup al arrancar y activa el timer de 12 h.
    /// </summary>
    public async Task IniciarAsync()
    {
        // Chequeo inmediato al arrancar
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
