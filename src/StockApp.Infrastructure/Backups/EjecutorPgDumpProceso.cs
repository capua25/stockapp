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
/// (default 300s).
///
/// SEGURIDAD: la contraseña NUNCA viaja como argumento de línea de comandos (visible en
/// `ps aux`/Task Manager de cualquier usuario de la máquina) — se pasa por la variable de
/// entorno PGPASSWORD del proceso hijo; host/puerto/usuario/base viajan como argumentos
/// separados (ArgumentList), no interpolados en un solo string (evita además el quoting del
/// shell). Ver decisión de diseño en el Task: no unit-testeada directamente (adaptador de I/O
/// real, mismo criterio que ServicioGuardadoArchivo) — cubierta por RestaurabilidadBackupTests.
/// </summary>
public sealed class EjecutorPgDumpProceso : IEjecutorPgDump
{
    private readonly string? _pgDumpPathOverride;
    private readonly TimeSpan _timeout;
    private readonly ILogger<EjecutorPgDumpProceso> _logger;

    public EjecutorPgDumpProceso(IConfiguration configuration, ILogger<EjecutorPgDumpProceso> logger)
    {
        _pgDumpPathOverride = configuration["Backups:PgDumpPath"];
        var timeoutSegundos = configuration.GetValue<int?>("Backups:TimeoutSegundos") ?? 300;
        _timeout = TimeSpan.FromSeconds(timeoutSegundos);
        _logger = logger;
    }

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
            TryKill(proceso);
            return new ResultadoEjecucionPgDump(
                false, $"pg_dump excedió el timeout de {_timeout.TotalSeconds:0} segundos.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Binario no encontrado en el PATH ni en el override configurado.
            return new ResultadoEjecucionPgDump(
                false, $"No se pudo iniciar pg_dump ('{pgDumpPath}'): {ex.Message}");
        }
    }

    private void TryKill(Process proceso)
    {
        try
        {
            if (!proceso.HasExited)
                proceso.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException ex)
        {
            // El proceso ya había terminado entre el chequeo y el Kill (carrera benigna) — NO
            // es un error real, pero se loguea igual (pre-flight scan corregido): un kill
            // fallido que además siguiera vivo (el caso realmente anómalo, distinto de la
            // carrera benigna) no debe desaparecer sin dejar rastro. Va a stdout en esta
            // entrega (mismo criterio que el resto de los logs, Serilog llega en la E2).
            _logger.LogWarning(ex, "No se pudo matar el proceso pg_dump (pid {Pid}).", proceso.Id);
        }
    }
}
