namespace StockApp.Api.Backups;

/// <summary>
/// Evita que dos corridas de pg_dump corran al mismo tiempo (POST /backups, disparo manual):
/// el job automático (<see cref="BackupProgramadoService"/>) y el disparo manual
/// (<see cref="DisparadorBackupManual"/>) comparten esta única instancia Singleton -- registrada
/// así en Program.cs, no Scoped, porque el job y el request HTTP corren en scopes distintos y la
/// protección tiene que ser compartida entre ambos, no por-scope.
///
/// <see cref="TryEntrar"/> es no bloqueante (SemaphoreSlim.Wait(0)): el llamador decide qué
/// hacer si ya hay una corrida en curso -- el job la salta silenciosamente (log, reintenta en
/// la ventana siguiente), el endpoint responde 409 vía ReglaDeNegocioException. Ninguno de los
/// dos calla en una cola esperando: dos pg_dump simultáneos contra la misma base es exactamente
/// el tipo de cosa que rompe en producción, así que el segundo llamador se rechaza, no se demora.
/// </summary>
public interface IGuardiaCorridaBackup
{
    /// <summary>true si no había ninguna corrida en curso y esta llamada tomó el turno
    /// (el llamador DEBE invocar <see cref="Salir"/> cuando termine, éxito o no). false si ya
    /// había una corrida en curso -- no se tomó ningún turno, no hace falta llamar a Salir.</summary>
    bool TryEntrar();

    /// <summary>Libera el turno tomado por un <see cref="TryEntrar"/> que devolvió true.</summary>
    void Salir();
}

public sealed class GuardiaCorridaBackup : IGuardiaCorridaBackup
{
    private readonly SemaphoreSlim _semaforo = new(1, 1);

    public bool TryEntrar() => _semaforo.Wait(0);

    public void Salir() => _semaforo.Release();
}
