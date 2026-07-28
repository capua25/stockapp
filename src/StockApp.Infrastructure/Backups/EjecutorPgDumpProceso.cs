using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using StockApp.Application.Backups;

namespace StockApp.Infrastructure.Backups;

/// <summary>
/// Implementación real de IEjecutorPgDump (spec §4.2): invoca pg_dump como proceso hijo,
/// formato -Fc (custom, comprimido nativo — un dump plano se infla por los bytea de adjuntos
/// ya presentes en el esquema de Finanzas). La ruta del binario se resuelve por PATH del
/// proceso, con override por configuración Backups:PgDumpPath (spec §3 decisión 1) para el caso
/// en que no esté en el PATH del servicio. Timeout configurable vía Backups:TimeoutSegundos
/// (default 1800s / 30 minutos, ver más abajo).
///
/// SEGURIDAD: la contraseña NUNCA viaja como argumento de línea de comandos (visible en
/// `ps aux`/Task Manager de cualquier usuario de la máquina) — se pasa por la variable de
/// entorno PGPASSWORD del proceso hijo; host/puerto/usuario/base viajan como argumentos
/// separados (ArgumentList), no interpolados en un solo string (evita además el quoting del
/// shell). Ver decisión de diseño en el Task: no unit-testeada directamente (adaptador de I/O
/// real, mismo criterio que ServicioGuardadoArchivo) — cubierta por RestaurabilidadBackupTests.
///
/// Default de timeout subido a 30 minutos (fix del review final E1; antes 300s/5min): la base
/// guarda adjuntos PDF/JPG/PNG en bytea, y la única palanca para el timeout es
/// Backups:TimeoutSegundos en el appsettings del SERVIDOR -- que por la restricción rectora del
/// proyecto (sin acceso remoto post-instalación) nadie puede tocar. El CancellationToken del
/// host ya corta el proceso en el apagado (ver TryKill en el finally de EjecutarAsync), así que
/// este timeout sólo necesita proteger contra un cuelgue genuino, no ser agresivo.
/// </summary>
public sealed class EjecutorPgDumpProceso : IEjecutorPgDump
{
    private const int TimeoutSegundosDefault = 1800; // 30 minutos.

    private readonly string? _pgDumpPathOverride;
    private readonly TimeSpan _timeout;
    private readonly ILogger<EjecutorPgDumpProceso> _logger;

    public EjecutorPgDumpProceso(IConfiguration configuration, ILogger<EjecutorPgDumpProceso> logger)
    {
        // Fix (review final E1): un PgDumpPath configurado como string VACÍO (no ausente) hacía
        // que "?? "pg_dump"" no aplicara (una cadena vacía no es null) y Process.Start explotara
        // con FileName vacío. IsNullOrWhiteSpace trata "vacío" y "sólo espacios" igual que
        // "ausente".
        var pgDumpPathRaw = configuration["Backups:PgDumpPath"];
        _pgDumpPathOverride = string.IsNullOrWhiteSpace(pgDumpPathRaw) ? null : pgDumpPathRaw;

        var timeoutSegundosRaw = configuration["Backups:TimeoutSegundos"];
        var timeoutSegundos = TimeoutSegundosDefault;
        if (!string.IsNullOrEmpty(timeoutSegundosRaw))
        {
            // Fix (review final E1): 0 o negativo parseaba OK y explotaba recién después con
            // ArgumentOutOfRangeException desde CancelAfter -- mismo criterio de "fallar ruidoso
            // acá" que ya se usaba para el valor no parseable, ahora también para el rango.
            if (!int.TryParse(timeoutSegundosRaw, out timeoutSegundos) || timeoutSegundos <= 0)
            {
                throw new InvalidOperationException(
                    $"La configuración 'Backups:TimeoutSegundos' tiene un valor inválido: '{timeoutSegundosRaw}'. " +
                    "Debe ser un número entero de segundos mayor a cero.");
            }
        }
        _timeout = TimeSpan.FromSeconds(timeoutSegundos);
        _logger = logger;
    }

    /// <summary>Sólo para tests (InternalsVisibleTo a StockApp.Infrastructure.Tests): permite
    /// verificar el timeout y el path resueltos por el constructor sin depender de comportamiento
    /// observable en tiempo real (30 minutos es demasiado para esperar en un test).</summary>
    internal TimeSpan TimeoutParaPruebas => _timeout;

    internal string? PgDumpPathOverrideParaPruebas => _pgDumpPathOverride;

    public async Task<ResultadoEjecucionPgDump> EjecutarAsync(
        string connectionString, string rutaDestino, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var pgDumpPath = _pgDumpPathOverride ?? "pg_dump";

        using var proceso = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pgDumpPath,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        proceso.StartInfo.EnvironmentVariables["PGPASSWORD"] = builder.Password;
        proceso.StartInfo.ArgumentList.Add($"--host={builder.Host}");
        proceso.StartInfo.ArgumentList.Add($"--port={builder.Port}");
        proceso.StartInfo.ArgumentList.Add($"--username={builder.Username}");
        proceso.StartInfo.ArgumentList.Add($"--dbname={builder.Database}");
        proceso.StartInfo.ArgumentList.Add("--format=custom");
        proceso.StartInfo.ArgumentList.Add($"--file={rutaDestino}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            proceso.Start();
            var stderrTask = proceso.StandardError.ReadToEndAsync(timeoutCts.Token);
            await proceso.WaitForExitAsync(timeoutCts.Token);
            var stderr = await stderrTask;

            if (proceso.ExitCode != 0)
            {
                return new ResultadoEjecucionPgDump(false,
                    string.IsNullOrWhiteSpace(stderr)
                        ? $"pg_dump terminó con código {proceso.ExitCode}."
                        : stderr.Trim());
            }

            return new ResultadoEjecucionPgDump(true, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ResultadoEjecucionPgDump(
                false, $"pg_dump excedió el timeout de {_timeout.TotalSeconds:0} segundos.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Binario no encontrado en el PATH ni en el override configurado.
            return new ResultadoEjecucionPgDump(
                false, $"No se pudo iniciar pg_dump ('{pgDumpPath}'): {ex.Message}");
        }
        finally
        {
            // Fix (review final E1): antes TryKill sólo se llamaba desde la rama de timeout. Al
            // apagar el host, stoppingToken se cancela, el filtro "when (!cancellationToken.
            // IsCancellationRequested)" del catch de arriba da false a propósito, la
            // OperationCanceledException se propaga SIN pasar por ningún catch de acá -- y el
            // "using var proceso" liberaba el objeto Process sin matar al hijo, dejando un
            // pg_dump zombie escribiendo a un .tmp que el próximo arranque borra debajo suyo (en
            // Linux, el inode queda colgado ocupando disco). Un finally corre en TODA salida,
            // incluida esa propagación, así que ahora el hijo siempre se intenta matar.
            TryKill(proceso);
        }
    }

    private void TryKill(Process proceso)
    {
        try
        {
            if (!proceso.HasExited)
                proceso.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception or AggregateException)
        {
            // InvalidOperationException: el proceso nunca llegó a arrancar (Start() falló antes
            // de esta llamada -- rama Win32Exception de EjecutarAsync, o el finally corriendo
            // sobre un Process que jamás se inició) o ya había terminado entre el chequeo y el
            // Kill (carrera benigna). Win32Exception/AggregateException: Kill(entireProcessTree:
            // true) puede fallar por permisos del SO al recorrer el árbol de procesos, o agregar
            // varias fallas parciales al matar cada hijo (fix del review final E1: antes sólo se
            // atrapaba InvalidOperationException y estas dos escapaban, matando la corrida sin
            // registrar nada). Ninguna de las tres debe tumbar la corrida ni el arranque, pero
            // si el proceso realmente sigue vivo después de esto (el caso anómalo real, distinto
            // de la carrera benigna) no debe desaparecer sin dejar rastro — de ahí el log. No se
            // usa proceso.Id acá: en el caso "nunca arrancó", leer .Id también lanza
            // InvalidOperationException. Va a stdout en esta entrega (mismo criterio que el
            // resto de los logs, Serilog llega en la E2).
            _logger.LogWarning(ex, "No se pudo matar el proceso pg_dump.");
        }
    }
}
