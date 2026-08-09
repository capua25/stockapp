using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Canal de aviso hacia afuera del sistema ante el resultado de una corrida de backup.
/// CONTRATO INVIOLABLE: las implementaciones NUNCA propagan excepciones. El notificador es un
/// observador, no un participante — que se caiga la red no puede hacer fracasar un backup que
/// salió bien. Los puntos de enganche igual envuelven la llamada en try/catch (defensa en
/// profundidad), pero eso no exime a la implementación de cumplir el contrato.
/// </summary>
public interface INotificadorAlertas
{
    Task NotificarCorridaBackupAsync(CorridaBackup corrida, CancellationToken ct = default);
}
