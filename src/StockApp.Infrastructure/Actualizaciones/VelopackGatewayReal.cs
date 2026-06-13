using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace StockApp.Infrastructure.Actualizaciones;

/// <summary>
/// Adaptador delgado sobre <see cref="UpdateManager"/> real de Velopack.
/// Esta clase es el único punto que toca Velopack directamente.
/// No contiene lógica de negocio: solo traduce llamadas a la API de Velopack.
/// Absorbe <see cref="NotInstalledException"/> (app corriendo fuera del instalador).
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
}
