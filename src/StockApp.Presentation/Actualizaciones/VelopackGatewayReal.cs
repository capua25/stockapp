using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace StockApp.Presentation.Actualizaciones;

/// <summary>
/// Adaptador delgado sobre <see cref="UpdateManager"/> real de Velopack.
/// Esta clase es el único punto que toca Velopack directamente.
/// No contiene lógica de negocio: solo traduce llamadas a la API de Velopack.
/// Construye la fuente encadenada a partir de <see cref="UpdaterOptions"/>.
/// Absorbe <see cref="NotInstalledException"/> (app corriendo fuera del instalador).
/// NO es unit-testeable — validar manualmente en Bloque D (empaquetado).
/// </summary>
public sealed class VelopackGatewayReal : IVelopackGateway
{
    private readonly UpdateManager _manager;

    public VelopackGatewayReal(UpdaterOptions options)
    {
        var source = BuildSource(options);
        _manager = new UpdateManager(source);
    }

    /// <inheritdoc />
    public bool EstaInstalado => _manager.IsInstalled;

    /// <inheritdoc />
    public async Task<UpdateInfo?> BuscarUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            return await _manager.CheckForUpdatesAsync();
        }
        catch (NotInstalledException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task DescargarUpdateAsync(UpdateInfo update, Action<int>? progreso = null, CancellationToken ct = default)
        => _manager.DownloadUpdatesAsync(update, progreso, ct);

    /// <inheritdoc />
    public void AplicarYReiniciar(UpdateInfo update)
        => _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);

    // ── construcción de fuente encadenada ─────────────────────────────────────

    private static IUpdateSource BuildSource(UpdaterOptions options)
    {
        var github = new GithubSource(
            options.GitHubRepoUrl,
            options.GitHubAccessToken,
            options.GitHubPrerelease);

        // Si no hay feed propio configurado, usamos GitHub directamente (sin overhead de fallback).
        if (string.IsNullOrWhiteSpace(options.FeedPropiUrl))
            return github;

        var feedPropio = new SimpleWebSource(options.FeedPropiUrl);

        var fuentesOrdenadas = options.Orden == OrdenFuentes.FeedPropioPrimero
            ? new IUpdateSource[] { feedPropio, github }
            : new IUpdateSource[] { github, feedPropio };

        return new FallbackUpdateSource(fuentesOrdenadas);
    }
}
