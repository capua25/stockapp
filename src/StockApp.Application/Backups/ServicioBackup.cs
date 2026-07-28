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

        // Milisegundos en el nombre (no solo segundo): dos corridas dentro del mismo segundo ya
        // no colisionan en el rename atómico de abajo.
        var nombreArchivo = $"backup_{ahoraUtc:yyyyMMdd_HHmmssfff}.dump";
        var rutaFinal = Path.Combine(directorioBackups, nombreArchivo);
        var rutaTmp = rutaFinal + ".tmp";

        var resultado = await _ejecutor.EjecutarAsync(connectionString, rutaTmp, cancellationToken);

        CorridaBackup corrida;
        if (resultado.Exitoso)
        {
            try
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Principio rector de la entrega: nada falla en silencio. Sin este catch, la
                // excepción escapaba ANTES de _corridas.AgregarAsync -> ni Exitosa ni Fallida
                // quedaban registradas, y si la corrida siguiente tenía éxito, esta pérdida era
                // invisible para siempre (el banner de salud de 26h nunca se disparaba). Acotado
                // a IOException/UnauthorizedAccessException -las que File.Move realmente puede
                // lanzar por colisión de destino, disco lleno, permisos o un antivirus reteniendo
                // el handle en Windows- mismo criterio que BorrarSiExiste: un catch (Exception)
                // genérico taparía errores de programación que sí queremos que exploten.
                _logger.LogWarning(ex, "No se pudo mover el backup de '{RutaTmp}' a '{RutaFinal}'.", rutaTmp, rutaFinal);
                corrida = new CorridaBackup
                {
                    IniciadaEn = ahoraUtc,
                    FinalizadaEn = DateTime.UtcNow,
                    Resultado = ResultadoBackup.Fallida,
                    NombreArchivo = null,
                    TamanioBytes = null,
                    MotivoFallo = $"El dump se generó pero no se pudo mover a destino: {ex.Message}",
                };
            }
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
            // Fix (review final E1): antes se borraba la fila SIN importar si BorrarSiExiste
            // pudo borrar el archivo -- un .dump que un antivirus tenía tomado en la ventana de
            // retención quedaba huérfano en disco PARA SIEMPRE (LimpiarTmpHuerfanos sólo barre
            // .tmp). Ahora sólo se elimina la fila si el archivo efectivamente se borró (o ya no
            // existía); si el borrado falló, la fila sobrevive y la corrida siguiente reintenta.
            var archivoBorrado = corrida.NombreArchivo is null
                || BorrarSiExiste(Path.Combine(directorioBackups, corrida.NombreArchivo));
            if (archivoBorrado)
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

    /// <summary>Barrido de archivos .dump huérfanos: dumps en disco sin fila correspondiente en
    /// <see cref="ICorridaBackupRepository"/> (fix del review final E1). Disparador principal:
    /// RESTAURAR la base -propósito de toda esta feature- vuelve CorridasBackup al estado del
    /// dump restaurado, y todos los .dump generados DESPUÉS de ese punto quedan en disco sin
    /// fila que los referencie -- nada más en la entrega reconcilia disco contra DB. Llamado al
    /// arrancar BackupProgramadoService, junto a <see cref="LimpiarTmpHuerfanos"/>.
    ///
    /// Margen de gracia (<paramref name="margenDeGracia"/>, default 15 minutos): entre el rename
    /// atómico a .dump (spec §4.3) y el _corridas.AgregarAsync que le da su fila hay una ventana
    /// real, aunque chica, donde el archivo existe en disco sin fila todavía -- sin este margen,
    /// un barrido que corriera justo en ese instante borraría un backup recién creado y válido.
    /// 15 minutos es generoso frente a esa ventana (típicamente milisegundos: un insert en la
    /// misma base que ya está arriba) sin dejar de reconciliar con prontitud, dado que este
    /// barrido corre en cada arranque de la API.</summary>
    public async Task LimpiarDumpHuerfanosAsync(
        string directorioBackups, DateTime ahoraUtc, TimeSpan? margenDeGracia = null)
    {
        if (!Directory.Exists(directorioBackups))
            return;

        var margen = margenDeGracia ?? TimeSpan.FromMinutes(15);
        var nombresConocidos = (await _corridas.ListarTodasAsync())
            .Where(c => c.NombreArchivo is not null)
            .Select(c => c.NombreArchivo!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var ruta in Directory.GetFiles(directorioBackups, "*.dump"))
        {
            var nombre = Path.GetFileName(ruta);
            if (nombresConocidos.Contains(nombre))
                continue;

            var ultimaEscritura = File.GetLastWriteTimeUtc(ruta);
            if (ahoraUtc - ultimaEscritura < margen)
                continue; // Podría ser el rename atómico de una corrida en vuelo -- todavía no.

            BorrarSiExiste(ruta);
        }
    }

    /// <summary>Devuelve true si el archivo quedó borrado (o ya no existía), false si el borrado
    /// falló -- <see cref="AplicarRetencionAsync"/> usa este valor para decidir si es seguro
    /// eliminar también la fila de <see cref="ICorridaBackupRepository"/> (fix del review final
    /// E1: antes se borraba la fila SIEMPRE, sin importar si el archivo se pudo borrar).</summary>
    private bool BorrarSiExiste(string ruta)
    {
        try
        {
            if (File.Exists(ruta))
                File.Delete(ruta);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Mejor esfuerzo: un archivo bloqueado no debe tumbar la corrida ni el arranque.
            // UnauthorizedAccessException se suma a IOException porque File.Delete la lanza
            // cuando el archivo es de sólo lectura, faltan permisos, o el archivo está tomado
            // por otro proceso (antivirus incluido) — escenario realista en un servidor Windows,
            // no teórico. Acotado a estas dos excepciones a propósito: un catch (Exception)
            // genérico taparía errores de programación (null refs, argumentos inválidos) que sí
            // queremos que exploten.
            // LogWarning (no silencioso, pre-flight scan corregido): un .tmp/.dump que no se
            // pudo borrar es exactamente el caso que necesita diagnóstico — sin este log, un
            // huérfano que se acumula corrida tras corrida no deja ningún rastro. Va a stdout
            // en esta entrega (Serilog llega en la E2, que lo captura retroactivamente — no
            // hace falta tocar este código en la E2).
            _logger.LogWarning(ex, "No se pudo borrar el archivo '{Ruta}'.", ruta);
            return false;
        }
    }
}
