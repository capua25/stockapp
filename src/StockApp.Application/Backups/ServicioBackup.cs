using System.Globalization;
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
    private readonly INotificadorAlertas _notificador;
    private readonly ILogger<ServicioBackup> _logger;

    public ServicioBackup(
        IEjecutorPgDump ejecutor,
        ICorridaBackupRepository corridas,
        INotificadorAlertas notificador,
        ILogger<ServicioBackup> logger)
    {
        _ejecutor = ejecutor;
        _corridas = corridas;
        _notificador = notificador;
        _logger = logger;
    }

    /// <summary><paramref name="usuarioId"/> (fix/integridad-referencial): actor que pidió esta
    /// corrida, o null si la disparó el job automático (BackupProgramadoService no lo pasa —
    /// default null, sin cambiar su comportamiento). DisparadorBackupManual (Api, POST /backups)
    /// es el único llamador que lo pasa con valor.</summary>
    public async Task EjecutarCorridaAsync(
        string connectionString, string directorioBackups, DateTime ahoraUtc, CancellationToken cancellationToken,
        int? usuarioId = null)
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
                    UsuarioId = usuarioId,
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
                    UsuarioId = usuarioId,
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
                UsuarioId = usuarioId,
            };
        }

        await _corridas.AgregarAsync(corrida);

        await NotificarSinRomperAsync(corrida, cancellationToken);

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

    /// <summary>Marca en <see cref="CorridaBackup.MotivoFallo"/> de una fila reconstruida por
    /// <see cref="ReconciliarDumpHuerfanosAsync"/>. Se apoya en ese campo -en vez de agregar una
    /// columna nueva, lo que exigiría una migración a esta altura de la entrega- porque para una
    /// corrida <see cref="ResultadoBackup.Exitosa"/> real siempre es null: cualquier valor no-nulo
    /// ahí ya es, de por sí, una señal de "esto no salió de EjecutarCorridaAsync". Texto acortado
    /// (tercer review final E1, FIX IMPORTANT): la versión anterior decía "no proviene de una
    /// corrida real" -- cierto, pero un admin mirando esta lista justo después de restaurar la
    /// base para bajar su backup de seguridad lo lee como "este archivo no sirve". Presentation
    /// (FilaCorridaBackupVm.EsNotaInformativa) ya distingue esta nota de un fallo real, así que el
    /// texto no necesita cargar esa advertencia.</summary>
    internal const string MarcaFilaReconciliada =
        "[Reconciliado] Registro reconstruido desde el archivo en disco (posterior a una restauración).";

    private const string FormatoNombreArchivo = "yyyyMMdd_HHmmssfff";
    private const string PrefijoNombreArchivo = "backup_";
    private const string SufijoNombreArchivo = ".dump";

    /// <summary>Reconciliación disco↔DB de archivos .dump huérfanos: dumps en disco sin fila
    /// correspondiente en <see cref="ICorridaBackupRepository"/> (fix del re-review final E1).
    /// Disparador principal: RESTAURAR la base -propósito de toda esta feature- vuelve
    /// CorridasBackup al estado del dump restaurado, y todos los .dump generados DESPUÉS de ese
    /// punto quedan en disco sin fila que los referencie -- en el peor caso, incluido el backup de
    /// seguridad que el admin tomó recién ANTES de restaurar.
    ///
    /// Este método reemplaza a un barrido anterior (mismo nombre "Limpiar...") que directamente
    /// BORRABA esos archivos: contra el escenario de arriba, eso convertía una fuga de disco
    /// acotada en la destrucción de backups válidos en el momento exacto de recuperación ante
    /// desastre. Ahora, en cambio, se DA DE ALTA la fila que le falta al .dump -reconstruida a
    /// partir de su propio nombre y de la metadata del filesystem- y se deja que
    /// <see cref="PoliticaRetencion"/> decida su destino en la corrida siguiente, igual que
    /// cualquier otra corrida exitosa: sin fuga (la retención lo va a limpiar cuando corresponda)
    /// y sin pérdida (mientras tanto vuelve a ser descargable desde Mantenimiento).
    ///
    /// Un archivo cuyo nombre NO matchea el formato "backup_yyyyMMdd_HHmmssfff.dump" (ej. algo
    /// que un operador copió a mano en el directorio) nunca se borra NI se reconcilia -- no hay de
    /// dónde reconstruir su <see cref="CorridaBackup.IniciadaEn"/>, y el criterio es no destruir
    /// nunca un archivo que no se reconoce. Se loguea y se deja en disco para revisión manual.
    ///
    /// Fila reconstruida: Resultado = Exitosa (el archivo existe en disco), NombreArchivo = el
    /// propio nombre, TamanioBytes = el tamaño real en disco, IniciadaEn = el timestamp parseado
    /// del nombre (es el mismo ahoraUtc con el que EjecutarCorridaAsync lo generó originalmente),
    /// FinalizadaEn = LastWriteTimeUtc del archivo (aproxima el fin de la corrida real: el rename
    /// atómico ocurre a los pocos milisegundos/segundos de que pg_dump termina). MotivoFallo lleva
    /// <see cref="MarcaFilaReconciliada"/> -ver su doc- para poder distinguir esta fila de una
    /// corrida real sin agregar columnas a la tabla.
    ///
    /// Margen de gracia (<paramref name="margenDeGracia"/>, default 15 minutos): entre el rename
    /// atómico a .dump (spec §4.3) y el _corridas.AgregarAsync que le da su fila hay una ventana
    /// real, aunque chica, donde el archivo existe en disco sin fila todavía -- sin este margen,
    /// una reconciliación que corriera justo en ese instante daría de alta una fila DUPLICADA para
    /// un backup recién creado y válido (la fila real llega milisegundos después). 15 minutos es
    /// generoso frente a esa ventana sin dejar de reconciliar con prontitud, dado que este barrido
    /// corre en cada arranque de la API.</summary>
    public async Task ReconciliarDumpHuerfanosAsync(
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

            if (!TryParseIniciadaEn(nombre, out var iniciadaEn))
            {
                // Criterio del usuario: nunca borrar un archivo que no se reconoce. Se deja en
                // disco, sin fila, y el log es el único rastro para que un humano lo revise.
                _logger.LogWarning(
                    "Dump huérfano '{Nombre}' no matchea el formato esperado ({Prefijo}{Formato}{Sufijo}) -- se deja en disco sin reconciliar.",
                    nombre, PrefijoNombreArchivo, FormatoNombreArchivo, SufijoNombreArchivo);
                continue;
            }

            _logger.LogWarning(
                "Dump huérfano '{Nombre}' sin fila en CorridasBackup (probablemente por un restore) -- se reconcilia dando de alta su corrida.",
                nombre);
            await _corridas.AgregarAsync(new CorridaBackup
            {
                IniciadaEn = iniciadaEn,
                FinalizadaEn = ultimaEscritura,
                Resultado = ResultadoBackup.Exitosa,
                NombreArchivo = nombre,
                TamanioBytes = new FileInfo(ruta).Length,
                MotivoFallo = MarcaFilaReconciliada,
                // Nadie "pidió" esta corrida -- se reconstruyó de un .dump huérfano en disco.
                UsuarioId = null,
            });
        }
    }

    /// <summary>Intenta reconstruir el <see cref="CorridaBackup.IniciadaEn"/> original a partir del
    /// nombre de archivo que <see cref="EjecutarCorridaAsync"/> genera ("backup_{ahoraUtc:yyyyMMdd_HHmmssfff}.dump").
    /// false si el nombre no matchea ese formato exacto (prefijo/sufijo o marca de tiempo inválida).</summary>
    private static bool TryParseIniciadaEn(string nombreArchivo, out DateTime iniciadaEn)
    {
        iniciadaEn = default;
        if (!nombreArchivo.StartsWith(PrefijoNombreArchivo, StringComparison.OrdinalIgnoreCase)
            || !nombreArchivo.EndsWith(SufijoNombreArchivo, StringComparison.OrdinalIgnoreCase))
            return false;

        var marcaDeTiempo = nombreArchivo[PrefijoNombreArchivo.Length..^SufijoNombreArchivo.Length];
        return DateTime.TryParseExact(
            marcaDeTiempo, FormatoNombreArchivo, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out iniciadaEn);
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

    /// <summary>Defensa en profundidad: INotificadorAlertas ya se compromete a no propagar
    /// excepciones, pero notificar es best-effort y no puede tumbar una corrida que salió bien.
    /// Una implementación mal escrita del contrato no debería costarnos el backup.</summary>
    private async Task NotificarSinRomperAsync(CorridaBackup corrida, CancellationToken ct)
    {
        try
        {
            await _notificador.NotificarCorridaBackupAsync(corrida, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falló la notificación del resultado del backup.");
        }
    }
}
