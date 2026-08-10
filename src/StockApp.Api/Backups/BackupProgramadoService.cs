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
            else
                await EnviarHeartbeatDeArranqueAsync(stoppingToken);
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

            // Fix (IMPORTANTE, review final): este era el CUARTO camino de fallo sin notificación.
            // Solo se logueaba -- ni fila ni ping. Si Postgres tarda en levantar en cada reinicio,
            // el catch-up se pierde en cada boot sin ningún rastro hacia AFUERA del servidor, que
            // es exactamente el fallo silencioso que esta feature existe para eliminar. Mismo
            // bloque de aviso que EjecutarCorridaSeguraAsync: corrida sintética Fallida (no se
            // persiste, la BD puede ser justamente lo que está caído) en su propio try/catch.
            await NotificarFalloSinPersistirAsync(
                $"Fallo inesperado en la secuencia de arranque de backups: {ex.Message}");
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

    /// <summary>
    /// Heartbeat de arranque (fix IMPORTANTE del review final: la guía del dead man's switch
    /// producía FALSAS ALARMAS con el sistema sano).
    ///
    /// EL PROBLEMA: el <see cref="PeriodicTimer"/> se ancla al BOOT del proceso, no a la última
    /// corrida. Un reinicio a las 11h de la última corrida empuja la siguiente a t=23h; con la
    /// ventana de 14h que recomendaba el plan (12h + 2h de grace), healthchecks marcaba "down"
    /// con el sistema perfectamente sano. Y una falsa alarma es peor que no tener canal: entrena
    /// al usuario a ignorarlo.
    ///
    /// LA SOLUCIÓN: si al arrancar la última corrida exitosa está DENTRO de la ventana (o sea:
    /// no hay nada que hacer, el sistema está sano), se avisa igual que seguimos vivos. Así el
    /// hueco entre dos pings nunca supera el intervalo, por más reinicios que haya.
    ///
    /// POR QUÉ ESTO NO ROMPE EL DEAD MAN'S SWITCH: solo se manda cuando
    /// <see cref="DebeCorrerAhoraAsync"/> dice que NO hay que correr, es decir cuando hay una
    /// corrida exitosa de menos de 12h. Un proceso en crash-loop deja de calificar apenas esa
    /// corrida envejece más de 12h, y a partir de ahí ningún boot manda heartbeat: el check cae,
    /// que es lo correcto.
    ///
    /// Se traga sus propios errores: corre DENTRO del try de la secuencia de arranque, y un fallo
    /// acá no puede disfrazarse de "falló el arranque de backups" en el catch de afuera.
    /// </summary>
    internal async Task EnviarHeartbeatDeArranqueAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var corridas = scope.ServiceProvider.GetRequiredService<ICorridaBackupRepository>();
            var ultima = await corridas.ObtenerUltimaExitosaAsync();

            // Defensivo: DebeCorrerAhoraAsync ya devolvió false, así que acá siempre hay una.
            if (ultima is null)
                return;

            var notificador = scope.ServiceProvider.GetRequiredService<INotificadorAlertas>();
            await notificador.NotificarCorridaBackupAsync(ultima, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "No se pudo enviar el heartbeat de arranque al canal de alerta.");
        }
    }

    /// <summary>
    /// Arma una corrida sintética Fallida SOLO para tener algo que notificar y la manda por el
    /// canal de alerta, sin persistir nada. Los dos llamadores (la secuencia de arranque y el
    /// catch de última resistencia de una corrida) comparten el mismo motivo: son caminos que no
    /// dejan fila en la base, así que sin este aviso el fallo no existe para nadie de afuera.
    /// Nunca propaga: notificar es best-effort.
    /// </summary>
    private async Task NotificarFalloSinPersistirAsync(string motivo, CancellationToken ct = default)
    {
        try
        {
            await using var scopeAviso = _scopeFactory.CreateAsyncScope();
            var notificador = scopeAviso.ServiceProvider.GetRequiredService<INotificadorAlertas>();
            await notificador.NotificarCorridaBackupAsync(
                new CorridaBackup
                {
                    IniciadaEn = DateTime.UtcNow,
                    FinalizadaEn = DateTime.UtcNow,
                    Resultado = ResultadoBackup.Fallida,
                    MotivoFallo = motivo,
                },
                ct);
        }
        catch (Exception exAviso)
        {
            _logger.LogWarning(exAviso, "Además falló la notificación del fallo inesperado.");
        }
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
            await NotificarFalloSinPersistirAsync(
                $"Fallo inesperado en la corrida programada: {ex.Message}");
        }
        finally
        {
            _guardia.Salir();
        }
    }
}
