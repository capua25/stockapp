using StockApp.Application.Alertas;
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

    /// <summary>
    /// Ping de prueba contra una URL puntual, para el botón "Probar" de la pantalla de
    /// Mantenimiento.
    ///
    /// POR QUÉ ES UN MÉTODO APARTE Y NO NotificarCorridaBackupAsync (fix crítico del review
    /// final): el método de notificación devuelve <see cref="Task"/> a secas y se traga TODO por
    /// contrato, así que un verificador construido sobre él no puede observar NADA — respondía
    /// "se envió el ping" con la misma cara ante una URL con typo (404), un check borrado, un DNS
    /// caído o el egress bloqueado por el firewall del municipio. Un verificador que no puede
    /// fallar es un placebo: la premisa entera de la feature (que el sistema deje de fallar en
    /// silencio) quedaba rota adentro de su propia herramienta de diagnóstico.
    ///
    /// Este método SÍ devuelve el resultado real (status code incluido) y, aun así, TAMPOCO
    /// propaga excepciones: un fallo de red se reporta como <c>Exitoso = false</c>, nunca como
    /// una excepción que suba al endpoint.
    ///
    /// SSRF: viaja el status code y un mensaje PROPIO. Jamás el cuerpo de la respuesta remota
    /// — devolverlo convertiría el endpoint en un proxy de lectura hacia la red interna.
    /// </summary>
    Task<ResultadoPruebaAlertaDto> ProbarPingAsync(string url, CancellationToken ct = default);
}
