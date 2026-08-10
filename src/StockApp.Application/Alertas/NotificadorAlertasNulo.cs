using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;

namespace StockApp.Application.Alertas;

/// <summary>
/// Implementación no-op de <see cref="INotificadorAlertas"/>. Se usa en los tests que no
/// ejercitan la notificación, para no ensuciar cada construcción de SUT con un fake propio.
/// </summary>
public sealed class NotificadorAlertasNulo : INotificadorAlertas
{
    public Task NotificarCorridaBackupAsync(CorridaBackup corrida, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// Neutro y HONESTO: <c>Exitoso = false</c>. Este notificador no postea a ningún lado, así
    /// que devolver "true" sería exactamente el placebo que el método existe para eliminar.
    /// </summary>
    public Task<ResultadoPruebaAlertaDto> ProbarPingAsync(string url, CancellationToken ct = default)
        => Task.FromResult(new ResultadoPruebaAlertaDto(
            false, null, "No hay un canal de alerta real configurado en este entorno."));
}
