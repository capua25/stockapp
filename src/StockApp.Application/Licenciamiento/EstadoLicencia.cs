namespace StockApp.Application.Licenciamiento;

/// <summary>
/// Estado de licencia cacheado en memoria (singleton). Se calcula una vez al arranque y se
/// actualiza en la activación; el middleware de bloqueo lo lee con costo cero por request.
/// Thread-safe con lock simple (lo leen requests concurrentes).
/// </summary>
public sealed class EstadoLicencia
{
    private readonly object _lock = new();
    private bool _activada;
    private string _codigoMaquina = "";

    public bool Activada
    {
        get { lock (_lock) return _activada; }
        set { lock (_lock) _activada = value; }
    }

    public string CodigoMaquina
    {
        get { lock (_lock) return _codigoMaquina; }
        set { lock (_lock) _codigoMaquina = value; }
    }
}
