using System;
using System.Collections.Generic;
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
/// Construye la fuente (el feed propio de la API, envuelta en <see cref="FuenteFeedRegistrado"/>
/// para registrar el feed consultado) a partir de <see cref="UpdaterOptions"/>.
/// Absorbe <see cref="NotInstalledException"/> (app corriendo fuera del instalador).
/// NO es unit-testeable — validar manualmente en Bloque D (empaquetado).
/// </summary>
public sealed class VelopackGatewayReal : IVelopackGateway
{
    private readonly UpdateManager _manager;
    private readonly FuenteFeedRegistrado _fuente;

    public VelopackGatewayReal(UpdaterOptions options)
    {
        _fuente = BuildSource(options);
        _manager = new UpdateManager(_fuente);
    }

    /// <inheritdoc />
    public bool EstaInstalado => _manager.IsInstalled;

    /// <inheritdoc />
    public SemanticVersion? VersionInstalada => _manager.CurrentVersion;

    /// <inheritdoc />
    public IReadOnlyList<VelopackAsset> UltimoFeed => _fuente.UltimoFeed;

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

    // ── construcción de la fuente ─────────────────────────────────────────────

    private static FuenteFeedRegistrado BuildSource(UpdaterOptions options)
        => new(new SimpleWebSource(options.FeedUrl));
}
