namespace StockApp.Configurador.Servicios;

/// <summary>
/// Los tres casos de "Probar conexión" (spec 2026-08-20). NoResponde es, con diferencia, el
/// más frecuente en la práctica (servidor apagado, IP equivocada, firewall) — su mensaje en
/// la ventana es el que más se tiene que entender sin ayuda.
/// </summary>
public enum ResultadoPruebaConexion
{
    /// <summary>HTTP 200 y el cuerpo es el JSON esperado de GET / de StockApp.Api (status: "ok").</summary>
    Ok,

    /// <summary>Hubo respuesta HTTP, pero no es la API de Gestión Municipal (otro status, otro puerto ocupado por otra cosa).</summary>
    RespondeOtraCosa,

    /// <summary>Timeout, conexión rechazada, DNS, o cualquier otra falla de red.</summary>
    NoResponde,
}
