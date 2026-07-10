namespace StockApp.ApiClient;

/// <summary>
/// El servidor de StockApp no respondió: conexión rechazada, host inalcanzable o timeout
/// (spec 3b, "Manejo de errores"). Mensaje accionable pensado para mostrarse tal cual al
/// usuario en los ViewModels (que muestran ex.Message) y en la red global de App.axaml.cs.
/// </summary>
public class ServidorNoDisponibleException : Exception
{
    public const string MensajePorDefecto =
        "No se pudo conectar con el servidor de StockApp. " +
        "Verificá que el servidor esté encendido y accesible en la red, y volvé a intentar.";

    public ServidorNoDisponibleException(Exception? inner = null)
        : base(MensajePorDefecto, inner)
    {
    }
}
