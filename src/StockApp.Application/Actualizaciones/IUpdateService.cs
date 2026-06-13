namespace StockApp.Application.Actualizaciones;

/// <summary>
/// Contrato del actualizador. Application NO conoce Velopack: devuelve DTOs propios.
/// La implementación (Infrastructure) adapta UpdateManager detrás de IVelopackGateway.
/// </summary>
public interface IUpdateService
{
    /// <summary>Consulta la fuente encadenada. Null-safe: si no hay update, HayUpdate=false.</summary>
    Task<UpdateCheckResult> BuscarAsync(CancellationToken ct = default);

    /// <summary>Descarga el delta del update encontrado. Reporta progreso 0..100.</summary>
    Task DescargarAsync(IProgress<UpdateProgress>? progreso = null, CancellationToken ct = default);

    /// <summary>Aplica el update descargado y reinicia (Windows). No retorna en éxito.</summary>
    void AplicarYReiniciar();
}
