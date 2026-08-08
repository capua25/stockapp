using Microsoft.Extensions.Configuration;
using StockApp.Application.Backups;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Platform;

namespace StockApp.Api.Backups;

/// <summary>
/// Dispara una corrida de backup manual bajo demanda (POST /backups). Singleton -- mismo motivo
/// que BackupProgramadoService: crea su PROPIO scope por corrida (AppDbContext es Scoped), así
/// que la ejecución en background (fire-and-forget, iniciada desde un request HTTP que ya
/// respondió 202) nunca captura un servicio scoped que sobrevive al request. Comparte
/// IGuardiaCorridaBackup con BackupProgramadoService: nunca dos pg_dump al mismo tiempo, sea el
/// job automático o un disparo manual (incluido un doble click).
/// </summary>
public sealed class DisparadorBackupManual
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserDataPathProvider _paths;
    private readonly IGuardiaCorridaBackup _guardia;
    private readonly ILogger<DisparadorBackupManual> _logger;

    public DisparadorBackupManual(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IUserDataPathProvider paths,
        IGuardiaCorridaBackup guardia,
        ILogger<DisparadorBackupManual> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _paths = paths;
        _guardia = guardia;
        _logger = logger;
    }

    /// <summary>
    /// Intenta iniciar una corrida manual en background. Devuelve false (y no dispara nada,
    /// no toca el gate) si ya hay una corrida en curso -- el llamador (BackupsEndpoints)
    /// traduce eso a un 409 vía ReglaDeNegocioException.
    /// </summary>
    public bool Disparar(int usuarioId)
    {
        if (!_guardia.TryEntrar())
            return false;

        // Fix (review adversarial, IMPORTANTE 3 -- fuga de semáforo): connectionString/directorio
        // ahora se resuelven DENTRO de un try/catch que libera la guardia si algo explota ACÁ.
        // Antes corrían sueltas entre el TryEntrar() de arriba y el fire-and-forget de abajo: si
        // ConnectionStrings:Default faltaba, o IUserDataPathProvider.GetBackupsDirectory()
        // explotaba (deploy-vps.md: puede tirar o devolver "" si $HOME/.local/share no existe --
        // ya fue causa real de un bug de deploy), la excepción salía de Disparar() SIN que
        // EjecutarEnBackgroundAsync llegara a lanzarse -- y su finally { _guardia.Salir(); } es el
        // ÚNICO lugar que libera el turno. El semáforo Singleton quedaba tomado para siempre: ni
        // backups manuales (409 eterno) ni el job automático (BackupProgramadoService se salta
        // cada tick con un log, sin ninguna alarma) volvían a correr hasta reiniciar el proceso.
        try
        {
            var connectionString = _configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("Falta ConnectionStrings:Default.");
            var directorio = _paths.GetBackupsDirectory();

            // Fire-and-forget deliberado: el endpoint ya devolvió 202 antes de que esto termine
            // (un pg_dump puede tardar minutos). _ = descarta la Task a propósito -- el resultado
            // de la corrida se consulta después vía GET /backups, no por este request. A partir de
            // acá, _guardia.Salir() es responsabilidad EXCLUSIVA del finally de
            // EjecutarEnBackgroundAsync -- llamarlo también acá liberaría un turno que esa Task
            // todavía necesita.
            _ = EjecutarEnBackgroundAsync(connectionString, directorio, usuarioId);
            return true;
        }
        catch
        {
            _guardia.Salir();
            throw;
        }
    }

    private async Task EjecutarEnBackgroundAsync(string connectionString, string directorio, int usuarioId)
    {
        try
        {
            Directory.CreateDirectory(directorio);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var servicio = scope.ServiceProvider.GetRequiredService<ServicioBackup>();

            await servicio.EjecutarCorridaAsync(
                connectionString, directorio, DateTime.UtcNow, CancellationToken.None, usuarioId);
        }
        catch (Exception ex)
        {
            // Misma red de última resistencia que BackupProgramadoService.EjecutarCorridaSeguraAsync:
            // ServicioBackup ya captura los fallos esperables de pg_dump y los persiste como
            // CorridaBackup Fallida; esto solo cubre un error realmente inesperado. No hay
            // request HTTP vivo esperando esta Task -- el log ya no es el único rastro (ver
            // PersistirFallaAsync, IMPORTANTE 6 del review).
            _logger.LogError(ex, "Backup manual disparado por el usuario {UsuarioId} falló de forma inesperada.", usuarioId);
            await PersistirFallaAsync(ex, usuarioId);
        }
        finally
        {
            _guardia.Salir();
        }
    }

    /// <summary>
    /// Fix (review adversarial, IMPORTANTE 6): antes, un fallo inesperado acá arriba sólo se
    /// logueaba -- BackupsEndpoints le dijo al usuario "Backup iniciado en el servidor. Actualizá
    /// esta pantalla en unos minutos", y esa fila nunca aparecía en GET /backups (a diferencia de
    /// un fallo esperable de pg_dump, que ServicioBackup SÍ persiste). Este método deja rastro
    /// para el humano que apretó el botón. Usa un scope NUEVO (no el de EjecutarEnBackgroundAsync,
    /// que puede ni haberse creado si el fallo fue resolviendo ServicioBackup vía DI) -- y si
    /// PERSISTIR la falla también falla (ej. la propia base de la app está caída, que es
    /// justamente un motivo típico de fallo acá), sólo loguea el error de persistencia sin
    /// enmascarar el original: <paramref name="ex"/> ya quedó logueado por el llamador.
    /// </summary>
    private async Task PersistirFallaAsync(Exception ex, int usuarioId)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var corridas = scope.ServiceProvider.GetRequiredService<ICorridaBackupRepository>();
            var ahoraUtc = DateTime.UtcNow;

            await corridas.AgregarAsync(new CorridaBackup
            {
                IniciadaEn = ahoraUtc,
                FinalizadaEn = ahoraUtc,
                Resultado = ResultadoBackup.Fallida,
                NombreArchivo = null,
                TamanioBytes = null,
                MotivoFallo = ex.Message,
                UsuarioId = usuarioId,
            });
        }
        catch (Exception persistenciaEx)
        {
            _logger.LogError(
                persistenciaEx,
                "No se pudo persistir la CorridaBackup Fallida del backup manual disparado por el " +
                "usuario {UsuarioId} (motivo original del fallo: {MotivoOriginal}).",
                usuarioId, ex.Message);
        }
    }
}
