namespace StockApp.Application.Backups;

/// <summary>Resultado de una ejecución de pg_dump (spec Backups §4.2).</summary>
public sealed record ResultadoEjecucionPgDump(bool Exitoso, string? MensajeError);

/// <summary>
/// Abstracción del proceso hijo pg_dump (mismo espíritu que IFingerprintMaquina/IAlmacenLicencia
/// en Licenciamiento/: interfaz en Application, adaptador real en Infrastructure). Nunca lanza
/// por fallos esperables del proceso (binario ausente, credenciales rechazadas, timeout, disco
/// lleno) — los reporta en el resultado para que ServicioBackup los persista como CorridaBackup
/// Fallida sin interrumpir el BackgroundService.
/// </summary>
public interface IEjecutorPgDump
{
    Task<ResultadoEjecucionPgDump> EjecutarAsync(
        string connectionString, string rutaDestino, CancellationToken cancellationToken);
}
