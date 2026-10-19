using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace StockApp.Presentation.Actualizaciones;

/// <summary>
/// Decorador de <see cref="IUpdateSource"/> que recuerda los assets del último feed devuelto.
/// Existe porque <c>UpdateInfo.DeltasToTarget</c> queda vacío en Velopack 1.2.0 si no hay
/// paquete local base o el feed no trae un delta del destino: para conocer las releases
/// intermedias hay que leer el feed que devolvió la fuente.
/// </summary>
public sealed class FuenteFeedRegistrado : IUpdateSource
{
    private readonly IUpdateSource _interna;

    public FuenteFeedRegistrado(IUpdateSource interna)
    {
        _interna = interna;
    }

    /// <summary>Assets del último feed consultado; vacío hasta la primera consulta.</summary>
    public IReadOnlyList<VelopackAsset> UltimoFeed { get; private set; } = Array.Empty<VelopackAsset>();

    /// <inheritdoc />
    public async Task<VelopackAssetFeed> GetReleaseFeed(
        IVelopackLogger logger,
        string? appId,
        string channel,
        Guid? stagingId,
        VelopackAsset? latestLocalRelease)
    {
        var feed = await _interna.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease);
        UltimoFeed = feed.Assets;
        return feed;
    }

    /// <inheritdoc />
    public Task DownloadReleaseEntry(
        IVelopackLogger logger,
        VelopackAsset releaseEntry,
        string localFile,
        Action<int> progress,
        CancellationToken cancelToken)
        => _interna.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
}
