using Microsoft.Extensions.Configuration;
using StockApp.Application.Backups;
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

        var connectionString = _configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Default.");
        var directorio = _paths.GetBackupsDirectory();

        // Fire-and-forget deliberado: el endpoint ya devolvió 202 antes de que esto termine
        // (un pg_dump puede tardar minutos). _ = descarta la Task a propósito -- el resultado
        // de la corrida se consulta después vía GET /backups, no por este request.
        _ = EjecutarEnBackgroundAsync(connectionString, directorio, usuarioId);
        return true;
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
            // request HTTP vivo esperando esta Task -- el único rastro posible es el log.
            _logger.LogError(ex, "Backup manual disparado por el usuario {UsuarioId} falló de forma inesperada.", usuarioId);
        }
        finally
        {
            _guardia.Salir();
        }
    }
}
