using System;
using System.Threading;
using System.Threading.Tasks;
using StockApp.Application.Actualizaciones;
using Velopack;

namespace StockApp.Presentation.Actualizaciones;

/// <summary>
/// Implementación de <see cref="IUpdateService"/> que orquesta el flujo de actualización
/// usando <see cref="IVelopackGateway"/> como abstracción sobre Velopack.
/// Responsabilidades:
///   - Cortocircuita si la app no está instalada (dev/test).
///   - Lee <c>NotesMarkdown</c> del <see cref="VelopackAsset"/> y delega a <see cref="SeverityParser"/>;
///     la severidad efectiva es el máximo entre las releases posteriores a la instalada y el destino.
///   - Guarda el <see cref="UpdateInfo"/> encontrado para las operaciones DescargarAsync/AplicarYReiniciar.
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private readonly IVelopackGateway _gateway;
    private readonly SeverityParser _severityParser;
    private UpdateInfo? _updatePendiente;

    public VelopackUpdateService(IVelopackGateway gateway)
    {
        _gateway = gateway;
        _severityParser = new SeverityParser();
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> BuscarAsync(CancellationToken ct = default)
    {
        if (!_gateway.EstaInstalado)
            return UpdateCheckResult.SinUpdate;

        var updateInfo = await _gateway.BuscarUpdateAsync(ct);

        if (updateInfo is null)
            return UpdateCheckResult.SinUpdate;

        _updatePendiente = updateInfo;

        var asset = updateInfo.TargetFullRelease;
        var severity = SeveridadEfectiva(asset);
        var version = asset.Version?.ToString();

        return new UpdateCheckResult(
            HayUpdate: true,
            Version: version,
            Severity: severity,
            NotasMarkdown: asset.NotesMarkdown);
    }

    /// <summary>
    /// Máximo de severidad entre el destino y toda release posterior a la instalada hasta el destino
    /// (una intermedia crítica no se pierde aunque el destino sea normal). Sin versión instalada
    /// o sin feed registrado, cuenta solo el destino.
    /// </summary>
    private UpdateSeverity SeveridadEfectiva(VelopackAsset destino)
    {
        var severidad = _severityParser.Parse(destino.NotesMarkdown);
        var instalada = _gateway.VersionInstalada;
        if (instalada is null || destino.Version is null)
            return severidad;

        foreach (var release in _gateway.UltimoFeed ?? Array.Empty<VelopackAsset>())
        {
            if (release.Version is null
                || release.Version <= instalada
                || release.Version > destino.Version)
                continue;

            var s = _severityParser.Parse(release.NotesMarkdown);
            if (s > severidad)
                severidad = s;
        }

        return severidad;
    }

    /// <inheritdoc />
    public async Task DescargarAsync(IProgress<UpdateProgress>? progreso = null, CancellationToken ct = default)
    {
        if (_updatePendiente is null)
            throw new InvalidOperationException(
                "No hay update disponible. Llamar a BuscarAsync() antes de DescargarAsync().");

        Action<int>? onProgreso = progreso is null
            ? null
            : pct => progreso.Report(new UpdateProgress(pct));

        await _gateway.DescargarUpdateAsync(_updatePendiente, onProgreso, ct);
    }

    /// <inheritdoc />
    public void AplicarYReiniciar()
    {
        if (_updatePendiente is null)
            throw new InvalidOperationException(
                "No hay update descargado. Llamar a DescargarAsync() antes de AplicarYReiniciar().");

        _gateway.AplicarYReiniciar(_updatePendiente);
    }
}
