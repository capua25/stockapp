using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace StockApp.Infrastructure.Actualizaciones;

/// <summary>
/// <see cref="IUpdateSource"/> encadenada: intenta cada fuente en orden.
/// Si la primaria falla (cualquier excepción), usa la siguiente.
/// Si todas fallan, propaga la excepción de la última.
/// El orden de fuentes viene de configuración (<see cref="UpdaterOptions"/>).
/// </summary>
public sealed class FallbackUpdateSource : IUpdateSource
{
    private readonly IReadOnlyList<IUpdateSource> _fuentes;

    /// <param name="fuentes">
    /// Fuentes ordenadas de mayor a menor prioridad.
    /// Debe contener al menos una fuente.
    /// </param>
    public FallbackUpdateSource(IEnumerable<IUpdateSource> fuentes)
    {
        _fuentes = fuentes.ToList();
        if (_fuentes.Count == 0)
            throw new ArgumentException("Se requiere al menos una fuente de actualización.", nameof(fuentes));
    }

    /// <inheritdoc />
    public async Task<VelopackAssetFeed> GetReleaseFeed(
        IVelopackLogger logger,
        string? appId,
        string channel,
        Guid? stagingId,
        VelopackAsset? latestLocalRelease)
    {
        Exception? ultima = null;

        foreach (var fuente in _fuentes)
        {
            try
            {
                return await fuente.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease);
            }
            catch (Exception ex)
            {
                ultima = ex;
            }
        }

        throw ultima!;
    }

    /// <inheritdoc />
    public async Task DownloadReleaseEntry(
        IVelopackLogger logger,
        VelopackAsset releaseEntry,
        string localFile,
        Action<int> progress,
        CancellationToken cancelToken)
    {
        Exception? ultima = null;

        foreach (var fuente in _fuentes)
        {
            try
            {
                await fuente.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
                return;
            }
            catch (Exception ex)
            {
                ultima = ex;
            }
        }

        throw ultima!;
    }
}
