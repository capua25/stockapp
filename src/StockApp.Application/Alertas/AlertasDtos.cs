namespace StockApp.Application.Alertas;

/// <summary>Estado del canal de alerta tal como lo ve el desktop.</summary>
public sealed record ConfiguracionAlertasDto(string? UrlWebhook, bool Habilitado, DateTime? ActualizadoEn);

/// <summary>
/// Resultado de un ping de prueba. Viaja el status code pero NUNCA el cuerpo de la respuesta:
/// el servidor postea a una URL que provee el usuario (SSRF), y devolver el body convertiría el
/// endpoint en un proxy de lectura hacia la red interna.
/// </summary>
public sealed record ResultadoPruebaAlertaDto(bool Exitoso, int? StatusCode, string? Mensaje);
