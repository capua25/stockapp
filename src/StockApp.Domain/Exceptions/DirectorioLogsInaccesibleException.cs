namespace StockApp.Domain.Exceptions;

/// <summary>
/// Se lanza cuando el directorio de logs del servidor existe pero el proceso no puede
/// leerlo (permisos del filesystem). Entrega 2 (diagnóstico): antes, ServicioConsultaLogs
/// dejaba escapar el UnauthorizedAccessException crudo de System.IO.Directory.GetFiles, y
/// DomainExceptionHandler lo mapeaba a 403 "Prohibido." — un mensaje que hace pensar al
/// admin que SU cuenta no tiene permiso, cuando el problema real es que el servidor no
/// puede leer su propio directorio de logs. Esta excepción existe para que el handler
/// pueda distinguir ese caso (503, con la ruta en el mensaje) de un 403 real de usuario.
/// </summary>
public class DirectorioLogsInaccesibleException : Exception
{
    public DirectorioLogsInaccesibleException(string mensaje) : base(mensaje)
    {
    }

    public DirectorioLogsInaccesibleException(string mensaje, Exception innerException)
        : base(mensaje, innerException)
    {
    }
}
