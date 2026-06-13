using Velopack;
using Velopack.Sources;

namespace StockApp.Infrastructure.Actualizaciones;

/// <summary>
/// Adaptador delgado sobre <see cref="UpdateManager"/> real de Velopack.
/// Esta clase es el único punto que toca Velopack directamente.
/// No contiene lógica de negocio: solo traduce llamadas a la API de Velopack.
/// NO es unit-testeable — validar manualmente en Bloque D (empaquetado).
/// </summary>
public sealed class VelopackGatewayReal : IVelopackGateway
{
    private readonly UpdateManager _manager;

    public VelopackGatewayReal(IUpdateSource source)
    {
        _manager = new UpdateManager(source);
    }

    /// <inheritdoc />
    public bool EstaInstalado => _manager.IsInstalled;

    /// <inheritdoc />
    public Task<UpdateInfo?> BuscarUpdateAsync(CancellationToken ct = default)
        => _manager.CheckForUpdatesAsync();

    /// <inheritdoc />
    public Task DescargarUpdateAsync(UpdateInfo update, Action<int>? progreso = null, CancellationToken ct = default)
        => _manager.DownloadUpdatesAsync(update, progreso, ct);

    /// <inheritdoc />
    public void AplicarYReiniciar(UpdateInfo update)
        => _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
}
