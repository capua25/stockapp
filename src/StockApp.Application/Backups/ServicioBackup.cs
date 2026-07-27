using Microsoft.Extensions.Logging;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Backups;

/// <summary>
/// Orquesta una corrida de backup (spec §4.2): dump -> registrar corrida -> aplicar
/// PoliticaRetencion -> borrar huérfanos en disco. No conoce Process ni timers — eso vive en
/// IEjecutorPgDump y en BackupProgramadoService (Task 5) respectivamente. connectionString y
/// directorioBackups entran como parámetros (no inyectados) porque Application no puede
/// referenciar IConfiguration de la API ni IUserDataPathProvider de Infrastructure — ver
/// decisión de diseño documentada en el plan.
/// </summary>
public sealed class ServicioBackup
{
    private readonly IEjecutorPgDump _ejecutor;
    private readonly ICorridaBackupRepository _corridas;
    private readonly ILogger<ServicioBackup> _logger;

    public ServicioBackup(IEjecutorPgDump ejecutor, ICorridaBackupRepository corridas, ILogger<ServicioBackup> logger)
    {
        _ejecutor = ejecutor;
        _corridas = corridas;
        _logger = logger;
    }

    public async Task EjecutarCorridaAsync(
        string connectionString, string directorioBackups, DateTime ahoraUtc, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directorioBackups);

        var nombreArchivo = $"backup_{ahoraUtc:yyyyMMdd_HHmmss}.dump";
        var rutaFinal = Path.Combine(directorioBackups, nombreArchivo);
        var rutaTmp = rutaFinal + ".tmp";

        var resultado = await _ejecutor.EjecutarAsync(connectionString, rutaTmp, cancellationToken);

        CorridaBackup corrida;
        if (resultado.Exitoso)
        {
            File.Move(rutaTmp, rutaFinal); // rename atómico al cerrar con éxito (spec §4.3)
            corrida = new CorridaBackup
            {
                IniciadaEn = ahoraUtc,
                FinalizadaEn = DateTime.UtcNow,
                Resultado = ResultadoBackup.Exitosa,
                NombreArchivo = nombreArchivo,
                TamanioBytes = new FileInfo(rutaFinal).Length,
                MotivoFallo = null,
            };
        }
        else
        {
            BorrarSiExiste(rutaTmp);
            _logger.LogWarning("Backup fallido: {Motivo}", resultado.MensajeError);
            corrida = new CorridaBackup
            {
                IniciadaEn = ahoraUtc,
                FinalizadaEn = DateTime.UtcNow,
                Resultado = ResultadoBackup.Fallida,
                NombreArchivo = null,
                TamanioBytes = null,
                MotivoFallo = resultado.MensajeError,
            };
        }

        await _corridas.AgregarAsync(corrida);

        if (corrida.Resultado == ResultadoBackup.Exitosa)
            await AplicarRetencionAsync(directorioBackups, ahoraUtc);
    }

    private async Task AplicarRetencionAsync(string directorioBackups, DateTime ahoraUtc)
    {
        var exitosas = await _corridas.ListarExitosasAsync();
        var aBorrar = PoliticaRetencion.DeterminarABorrar(exitosas, ahoraUtc);

        foreach (var corrida in aBorrar)
        {
            if (corrida.NombreArchivo is not null)
                BorrarSiExiste(Path.Combine(directorioBackups, corrida.NombreArchivo));
            await _corridas.EliminarAsync(corrida.Id);
        }
    }

    /// <summary>Barrido de archivos .tmp huérfanos (dump interrumpido a mitad, ej. el proceso
    /// murió antes del rename atómico). Llamado al arrancar BackupProgramadoService (Task 5).</summary>
    public void LimpiarTmpHuerfanos(string directorioBackups)
    {
        if (!Directory.Exists(directorioBackups))
            return;

        foreach (var tmp in Directory.GetFiles(directorioBackups, "*.tmp"))
            BorrarSiExiste(tmp);
    }

    private void BorrarSiExiste(string ruta)
    {
        try
        {
            if (File.Exists(ruta))
                File.Delete(ruta);
        }
        catch (IOException ex)
        {
            // Mejor esfuerzo: un archivo bloqueado no debe tumbar la corrida ni el arranque.
            // LogWarning (no silencioso, pre-flight scan corregido): un .tmp/.dump que no se
            // pudo borrar es exactamente el caso que necesita diagnóstico — sin este log, un
            // huérfano que se acumula corrida tras corrida no deja ningún rastro. Va a stdout
            // en esta entrega (Serilog llega en la E2, que lo captura retroactivamente — no
            // hace falta tocar este código en la E2).
            _logger.LogWarning(ex, "No se pudo borrar el archivo '{Ruta}'.", ruta);
        }
    }
}
