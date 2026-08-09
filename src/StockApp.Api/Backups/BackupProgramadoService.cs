using Microsoft.Extensions.Configuration;
using StockApp.Application.Backups;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Platform;

namespace StockApp.Api.Backups;

/// <summary>
/// PRIMER BackgroundService del repo (spec Backups §3 decisión 2: scheduler dentro de la API,
/// cero superficie de instalación nueva). Crea su PROPIO IServiceScope por corrida — ver
/// decisión de diseño del Task: AppDbContext es Scoped, este servicio es Singleton.
/// </summary>
public sealed class BackupProgramadoService : BackgroundService
{
    private static readonly TimeSpan IntervaloEntreCorridas = TimeSpan.FromHours(12);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserDataPathProvider _paths;
    private readonly IGuardiaCorridaBackup _guardia;
    private readonly ILogger<BackupProgramadoService> _logger;

    public BackupProgramadoService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IUserDataPathProvider paths,
        IGuardiaCorridaBackup guardia,
        ILogger<BackupProgramadoService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _paths = paths;
        _guardia = guardia;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Fix (review final E1): GetBackupsDirectory() quedaba fuera del try aunque no es
        // realmente "cómputo puro" desde la perspectiva de este método -- es la única línea de
        // la secuencia de arranque que no pasaba por la red de contención de abajo. Movido
        // adentro para que un IUserDataPathProvider que explote (path inválido, etc.) también
        // caiga en el catch y no tumbe el BackgroundService entero. Arranca en string.Empty
        // (nunca se usa si GetBackupsDirectory no explota, la línea de abajo lo pisa enseguida)
        // para que el timer de más abajo siga teniendo un valor con el que reintentar corrida
        // tras corrida -- mismo invariante que antes, ahora también cubierto por el catch.
        var directorio = string.Empty;

        try
        {
            directorio = _paths.GetBackupsDirectory();
            Directory.CreateDirectory(directorio);

            await using (var scopeArranque = _scopeFactory.CreateAsyncScope())
            {
                var servicio = scopeArranque.ServiceProvider.GetRequiredService<ServicioBackup>();

                // Barrido de .tmp huérfanos (spec §4.3): un dump interrumpido a mitad (ej. la API se
                // reinició) deja un .tmp que nadie más va a limpiar.
                servicio.LimpiarTmpHuerfanos(directorio);

                // Reconciliación de .dump huérfanos (fix del re-review final E1): tras restaurar
                // la base, CorridasBackup vuelve al estado del dump restaurado y los .dump
                // posteriores quedan en disco sin fila -- se reconcilian dando de alta su corrida
                // (nunca se borran), y PoliticaRetencion decide su destino en la corrida siguiente.
                await servicio.ReconciliarDumpHuerfanosAsync(directorio, DateTime.UtcNow);
            }

            // Catch-up al arrancar (spec §4.2): si la última corrida exitosa tiene más de 12h (o no
            // hay ninguna), dispara enseguida en vez de esperar el primer tick del PeriodicTimer —
            // cubre el caso "servidor apagado durante la ventana".
            if (await DebeCorrerAhoraAsync())
                await EjecutarCorridaSeguraAsync(directorio, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Misma red de última resistencia que EjecutarCorridaSeguraAsync, pero para la
            // secuencia de ARRANQUE: DebeCorrerAhoraAsync hace una llamada real a la BD, y si la
            // API arranca antes de que Postgres esté listo (o Postgres se reinicia), esto explota
            // ACÁ, fuera del try de EjecutarCorridaSeguraAsync. Sin este catch, la excepción sale
            // de ExecuteAsync y (con el HostOptions.BackgroundServiceExceptionBehavior default de
            // StopHost) tumba el host ENTERO — endpoints HTTP incluidos, en un servidor sin acceso
            // remoto. Logueamos y seguimos al timer: 12h después Postgres probablemente esté vivo.
            _logger.LogError(ex, "Secuencia de arranque de backups programados falló de forma inesperada; se reintentará en la próxima ventana.");
        }

        using var timer = new PeriodicTimer(IntervaloEntreCorridas);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await EjecutarCorridaSeguraAsync(directorio, stoppingToken);
        }
    }

    internal async Task<bool> DebeCorrerAhoraAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var corridas = scope.ServiceProvider.GetRequiredService<ICorridaBackupRepository>();
        var ultima = await corridas.ObtenerUltimaExitosaAsync();
        return ultima is null || DateTime.UtcNow - ultima.FinalizadaEn >= IntervaloEntreCorridas;
    }

    internal async Task EjecutarCorridaSeguraAsync(string directorio, CancellationToken stoppingToken)
    {
        // Concurrencia (fix/integridad-referencial): IGuardiaCorridaBackup es la MISMA instancia
        // Singleton que usa DisparadorBackupManual (POST /backups) — si un admin disparó un
        // backup manual justo cuando tiquea el PeriodicTimer, este tick se salta en vez de
        // arrancar un segundo pg_dump simultáneo contra la misma base. Se reintenta solo, en la
        // ventana siguiente (12h después) — no hay cola ni reintento inmediato.
        if (!_guardia.TryEntrar())
        {
            _logger.LogInformation(
                "Corrida de backup programado omitida: ya hay una corrida en curso (probablemente un backup manual).");
            return;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var servicio = scope.ServiceProvider.GetRequiredService<ServicioBackup>();
            var connectionString = _configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("Falta ConnectionStrings:Default.");

            await servicio.EjecutarCorridaAsync(connectionString, directorio, DateTime.UtcNow, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Red de última resistencia: ServicioBackup ya captura los fallos esperables de
            // pg_dump y los persiste como CorridaBackup Fallida (spec §4.3); esto solo cubre un
            // error realmente inesperado (ej. la propia BD de la app caída al registrar la
            // corrida). Nunca debe tumbar el BackgroundService — el PeriodicTimer sigue vivo y
            // reintenta en la ventana siguiente.
            _logger.LogError(ex, "Corrida de backup programado falló de forma inesperada.");

            // Este camino no persiste fila (a diferencia de DisparadorBackupManual): sin esta
            // notificación, una corrida programada que revienta antes de llegar a ServicioBackup
            // no deja ningún rastro hacia afuera. Se arma una corrida sintética solo para avisar.
            try
            {
                await using var scopeAviso = _scopeFactory.CreateAsyncScope();
                var notificador = scopeAviso.ServiceProvider.GetRequiredService<INotificadorAlertas>();
                await notificador.NotificarCorridaBackupAsync(new CorridaBackup
                {
                    IniciadaEn = DateTime.UtcNow,
                    FinalizadaEn = DateTime.UtcNow,
                    Resultado = ResultadoBackup.Fallida,
                    MotivoFallo = $"Fallo inesperado en la corrida programada: {ex.Message}",
                });
            }
            catch (Exception exAviso)
            {
                _logger.LogWarning(exAviso, "Además falló la notificación del fallo inesperado.");
            }
        }
        finally
        {
            _guardia.Salir();
        }
    }
}
