using Velopack;

namespace StockApp.Infrastructure.Actualizaciones;

/// <summary>
/// Abstracción fina sobre <see cref="UpdateManager"/> de Velopack.
/// Existe únicamente para hacer <see cref="VelopackUpdateService"/> unit-testeable
/// sin tocar el proceso real de actualización.
/// </summary>
public interface IVelopackGateway
{
    /// <summary>
    /// Indica si la app está instalada vía Velopack (false en dev/test).
    /// </summary>
    bool EstaInstalado { get; }

    /// <summary>
    /// Consulta la fuente encadenada. Devuelve <c>null</c> si no hay update disponible
    /// o si la app no está instalada (NotInstalledException se absorbe aquí, no se propaga).
    /// </summary>
    Task<UpdateInfo?> BuscarUpdateAsync(CancellationToken ct = default);

    /// <summary>
    /// Descarga el update previamente encontrado. Reporta progreso 0–100.
    /// </summary>
    Task DescargarUpdateAsync(UpdateInfo update, Action<int>? progreso = null, CancellationToken ct = default);

    /// <summary>
    /// Aplica el update descargado y reinicia la app. No retorna si tiene éxito.
    /// </summary>
    void AplicarYReiniciar(UpdateInfo update);
}
