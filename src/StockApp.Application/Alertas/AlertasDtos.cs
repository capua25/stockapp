namespace StockApp.Application.Alertas;

/// <summary>Estado del canal de alerta tal como lo ve el desktop.</summary>
public sealed record ConfiguracionAlertasDto(string? UrlWebhook, bool Habilitado, DateTime? ActualizadoEn);

/// <summary>
/// Resultado de un ping de prueba. Viaja el status code pero NUNCA el cuerpo de la respuesta:
/// el servidor postea a una URL que provee el usuario (SSRF), y devolver el body convertiría el
/// endpoint en un proxy de lectura hacia la red interna.
///
/// <c>Exitoso</c> refleja el <c>IsSuccessStatusCode</c> REAL de la respuesta remota, y
/// <c>StatusCode</c> el código obtenido — null solo cuando no hubo respuesta (DNS, timeout,
/// firewall). Hasta el fix del review final este record mentía: el docstring prometía el status
/// code y en producción siempre viajaba <c>(true, null, ...)</c> incondicionalmente.
/// </summary>
public sealed record ResultadoPruebaAlertaDto(bool Exitoso, int? StatusCode, string? Mensaje);
