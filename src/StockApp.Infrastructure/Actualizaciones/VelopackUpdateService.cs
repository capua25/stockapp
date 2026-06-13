using StockApp.Application.Actualizaciones;
using Velopack;

namespace StockApp.Infrastructure.Actualizaciones;

/// <summary>
/// Implementación de <see cref="IUpdateService"/> que orquesta el flujo de actualización
/// usando <see cref="IVelopackGateway"/> como abstracción sobre Velopack.
/// Responsabilidades:
///   - Cortocircuita si la app no está instalada (dev/test).
///   - Lee <c>NotesMarkdown</c> del <see cref="VelopackAsset"/> y delega a <see cref="SeverityParser"/>.
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
        var severity = _severityParser.Parse(asset.NotesMarkdown);
        var version = asset.Version?.ToString();

        return new UpdateCheckResult(
            HayUpdate: true,
            Version: version,
            Severity: severity,
            NotasMarkdown: asset.NotesMarkdown);
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
