using System.Text;
using Microsoft.Extensions.Logging;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Infrastructure.Notificaciones;

/// <summary>
/// Notifica el resultado de una corrida de backup posteando a una URL configurable, siguiendo
/// la convención de healthchecks.io: éxito a {url} (heartbeat), fallo a {url}/fail.
///
/// El heartbeat es lo que cubre el modo de falla realmente peligroso: si la API muere, no hay
/// backup, no hay error y no queda fila en la base — solo silencio, indistinguible de que todo
/// haya salido bien. Un servicio externo que espera el ping periódico es el único componente
/// que sigue vivo cuando el servidor no lo está.
/// </summary>
public sealed class NotificadorWebhook : INotificadorAlertas
{
    /// <summary>Healthchecks.io acepta cuerpos grandes, pero un stderr de pg_dump desbocado no
    /// aporta nada después de los primeros párrafos y sí puede hacer fallar el POST.</summary>
    private const int MaxCaracteresBody = 2000;

    private readonly HttpClient _http;
    private readonly IConfiguracionAlertasRepository _configuracion;
    private readonly ILogger<NotificadorWebhook> _logger;

    public NotificadorWebhook(
        HttpClient http,
        IConfiguracionAlertasRepository configuracion,
        ILogger<NotificadorWebhook> logger)
    {
        _http = http;
        _configuracion = configuracion;
        _logger = logger;
    }

    public async Task NotificarCorridaBackupAsync(CorridaBackup corrida, CancellationToken ct = default)
    {
        try
        {
            // Se lee en cada llamada, no se cachea: el backup corre cada 12 horas (el costo es
            // irrelevante) y así un cambio de URL toma efecto sin reiniciar el servidor — que es
            // justamente lo que no se puede hacer después de la instalación.
            var cfg = await _configuracion.ObtenerAsync();

            if (!cfg.Habilitado || string.IsNullOrWhiteSpace(cfg.UrlWebhook))
                return;

            // Resultado == Fallida, NUNCA MotivoFallo != null: esa columna es de doble propósito
            // y también marca corridas EXITOSAS reconciliadas desde disco huérfano. Filtrar por
            // ella dispararía alertas falsas después de cada restauración.
            var fallo = corrida.Resultado == ResultadoBackup.Fallida;

            var url = ConstruirUrl(cfg.UrlWebhook!, fallo);
            var body = ConstruirBody(corrida, fallo);

            using var contenido = new StringContent(body, Encoding.UTF8, "text/plain");
            var respuesta = await _http.PostAsync(url, contenido, ct);

            if (!respuesta.IsSuccessStatusCode)
                _logger.LogWarning(
                    "El webhook de alertas respondió {Status} al notificar la corrida {CorridaId}.",
                    (int)respuesta.StatusCode, corrida.Id);
        }
        catch (Exception ex)
        {
            // Se traga TODO a propósito, incluida la cancelación: notificar es best-effort y no
            // puede hacer fracasar una corrida de backup que salió bien. El log es el rastro.
            _logger.LogWarning(ex, "No se pudo notificar el resultado del backup al webhook configurado.");
        }
    }

    private static string ConstruirUrl(string urlBase, bool fallo)
    {
        var limpia = urlBase.Trim().TrimEnd('/');
        return fallo ? limpia + "/fail" : limpia;
    }

    private static string ConstruirBody(CorridaBackup corrida, bool fallo)
    {
        var texto = fallo
            ? $"Backup FALLIDO el {corrida.FinalizadaEn:yyyy-MM-dd HH:mm} UTC. Motivo: {corrida.MotivoFallo}"
            : $"Backup OK el {corrida.FinalizadaEn:yyyy-MM-dd HH:mm} UTC. Archivo: {corrida.NombreArchivo} ({corrida.TamanioBytes} bytes)";

        return texto.Length > MaxCaracteresBody ? texto[..MaxCaracteresBody] : texto;
    }
}
