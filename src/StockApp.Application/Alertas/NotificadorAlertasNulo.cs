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
}
