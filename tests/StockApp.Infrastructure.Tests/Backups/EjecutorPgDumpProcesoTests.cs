using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using StockApp.Infrastructure.Backups;
using StockApp.Tests.Compartido;
using Xunit;

namespace StockApp.Infrastructure.Tests.Backups;

/// <summary>
/// Tests unitarios de EjecutorPgDumpProceso enfocados en los fixes 4 y 6 del review final de
/// Entrega 1 (manejo del proceso hijo y timeout por default) — el resto de la clase sigue sin
/// unit-testearse directamente para el camino feliz de un dump real (eso lo cubre
/// RestaurabilidadBackupTests contra un Postgres real). Estos tests SÍ arrancan procesos reales
/// (un script /bin/sh que hace de "pg_dump" fake) porque el bug de Fix 4 -un pg_dump zombie tras
/// la cancelación del host- sólo es observable con un proceso hijo de verdad; un fake en memoria
/// no lo hubiera detectado nunca. Asume Linux/sh disponible (mismo criterio que
/// RestaurabilidadBackupTests, que ya asume postgresql-client en el PATH).
/// </summary>
public class EjecutorPgDumpProcesoTests : IDisposable
{
    private readonly string _directorioTrabajo =
        Path.Combine(Path.GetTempPath(), "EjecutorPgDumpProcesoTests_" + Guid.NewGuid());

    public EjecutorPgDumpProcesoTests() => Directory.CreateDirectory(_directorioTrabajo);

    public void Dispose()
    {
        if (Directory.Exists(_directorioTrabajo))
            Directory.Delete(_directorioTrabajo, recursive: true);
    }

    // ── Fix 6: default de timeout ───────────────────────────────────────────

    [Fact]
    public void Constructor_SinBackupsTimeoutSegundos_UsaDefaultDe30Minutos()
    {
        var ejecutor = new EjecutorPgDumpProceso(new ConfiguracionFake(), NullLogger<EjecutorPgDumpProceso>.Instance);

        Assert.Equal(TimeSpan.FromMinutes(30), ejecutor.TimeoutParaPruebas);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Constructor_TimeoutSegundosFueraDeRango_LanzaInvalidOperationException(string valor)
    {
        var configuracion = new ConfiguracionFake { ["Backups:TimeoutSegundos"] = valor };

        var ex = Record.Exception(() =>
            new EjecutorPgDumpProceso(configuracion, NullLogger<EjecutorPgDumpProceso>.Instance));

        Assert.IsType<InvalidOperationException>(ex);
    }

    // ── Fix menor: PgDumpPath vacío ─────────────────────────────────────────

    [Fact]
    public void Constructor_PgDumpPathVacioOEnBlanco_LoTrataComoAusente()
    {
        var configuracion = new ConfiguracionFake { ["Backups:PgDumpPath"] = "   " };

        var ejecutor = new EjecutorPgDumpProceso(configuracion, NullLogger<EjecutorPgDumpProceso>.Instance);

        Assert.Null(ejecutor.PgDumpPathOverrideParaPruebas);
    }

    // ── Fix 4: el proceso hijo no debe sobrevivir a la cancelación del host ──

    [Fact]
    public async Task EjecutarAsync_CancelacionDelHostMientrasCorre_MataElProcesoHijo()
    {
        var scriptPath = CrearScriptFakePgDumpQueEscribeSuPidYDuerme();
        var configuracion = new ConfiguracionFake { ["Backups:PgDumpPath"] = scriptPath };
        var ejecutor = new EjecutorPgDumpProceso(configuracion, NullLogger<EjecutorPgDumpProceso>.Instance);
        var rutaDestino = Path.Combine(_directorioTrabajo, "salida.dump");
        var pidFile = rutaDestino + ".pid";
        using var cts = new CancellationTokenSource();

        var tarea = ejecutor.EjecutarAsync(
            "Host=localhost;Port=5;Username=u;Password=p;Database=d", rutaDestino, cts.Token);

        // Esperar a que el script realmente haya arrancado (escribió su propio PID). El retorno
        // de HastaAsync NO se descarta: si el .pid no aparece en 5s, hay que fallar acá mismo con
        // un mensaje diagnosticable, en vez de dejar que reviente más abajo con un
        // FileNotFoundException que no dice qué se estaba esperando ni por qué.
        var pidFileAparecio = await EsperaMonotonica.HastaAsync(() => File.Exists(pidFile), TimeSpan.FromSeconds(5));
        Assert.True(pidFileAparecio,
            $"El script fake de pg_dump no escribió su .pid ({pidFile}) en 5s -- no llegó a arrancar.");
        var pid = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim());
        Assert.True(ProcesoSigueVivo(pid), "El script fake no llegó a arrancar -- test inválido.");

        // Simular el apagado del host: mismo stoppingToken que BackgroundService.ExecuteAsync le
        // pasa a EjecutarCorridaSeguraAsync -> ServicioBackup -> IEjecutorPgDump.EjecutarAsync.
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tarea);

        // El bug original (Fix 4): TryKill sólo se llamaba en la rama de timeout. Acá
        // cancellationToken.IsCancellationRequested es true, así que el catch de timeout NO
        // aplicaba (su propio filtro lo excluye a propósito) y la excepción se propagaba
        // mientras el "using var proceso" liberaba el objeto SIN matar al hijo real -- quedaba
        // un pg_dump zombie. Con el fix (finally), el proceso hijo debe estar muerto.
        var siguioVivo = !await EsperaMonotonica.HastaAsync(() => !ProcesoSigueVivo(pid), TimeSpan.FromSeconds(5));
        Assert.False(siguioVivo, $"El proceso hijo (pid {pid}) seguía vivo tras la cancelación -- quedó zombie.");
    }

    private string CrearScriptFakePgDumpQueEscribeSuPidYDuerme()
    {
        var scriptPath = Path.Combine(_directorioTrabajo, "fake-pg-dump.sh");
        // Extrae el valor de --file=... de sus propios argumentos (mismo formato que
        // EjecutorPgDumpProceso.EjecutarAsync arma vía ArgumentList) para saber dónde dejar el
        // .pid, escribe su propio PID ahí, y duerme -- simula un pg_dump colgado/en progreso.
        var script =
            "#!/bin/sh\n" +
            "file=\"\"\n" +
            "for arg in \"$@\"; do\n" +
            "  case \"$arg\" in\n" +
            "    --file=*) file=\"${arg#--file=}\" ;;\n" +
            "  esac\n" +
            "done\n" +
            "echo $$ > \"${file}.pid\"\n" +
            "sleep 60\n";
        File.WriteAllText(scriptPath, script);
        File.SetUnixFileMode(scriptPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return scriptPath;
    }

    private static bool ProcesoSigueVivo(int pid)
    {
        try
        {
            using var proceso = Process.GetProcessById(pid);
            return !proceso.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // No existe ningún proceso con ese pid -- ya terminó.
        }
    }

    /// <summary>IConfiguration fake respaldado por un diccionario en memoria -- mismo motivo que
    /// ConfiguracionVacia en RestaurabilidadBackupTests: este proyecto no referencia el paquete
    /// concreto Microsoft.Extensions.Configuration (sólo su .Abstractions transitivo), y el
    /// constraint global prohíbe agregar paquetes nuevos.</summary>
    private sealed class ConfiguracionFake : IConfiguration
    {
        private readonly Dictionary<string, string?> _valores = new();

        public string? this[string key]
        {
            get => _valores.GetValueOrDefault(key);
            set => _valores[key] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => throw new NotSupportedException();
        public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
    }
}
