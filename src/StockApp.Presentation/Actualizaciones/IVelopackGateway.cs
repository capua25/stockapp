using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Velopack;

namespace StockApp.Presentation.Actualizaciones;

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
    /// Versión instalada, o <c>null</c> si la app no está instalada vía Velopack.
    /// </summary>
    SemanticVersion? VersionInstalada { get; }

    /// <summary>
    /// Assets del último feed que devolvió la fuente (vacío antes de la primera consulta).
    /// </summary>
    IReadOnlyList<VelopackAsset> UltimoFeed { get; }

    /// <summary>
    /// Consulta el feed propio. Devuelve <c>null</c> si no hay update disponible
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
