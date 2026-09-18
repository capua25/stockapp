namespace StockApp.Configurador.Servicios;

/// <summary>
/// Los seis casos de "Probar conexión" (bug 2026-09-18, ver ProbadorConexion). Antes de este
/// fix, DNS que no resuelve, conexión rechazada, certificado TLS inválido, proxy interceptando
/// y timeout colapsaban en un único NoResponde, que además se mostraba con un mensaje que
/// AFIRMABA "el servidor está apagado" sin haberlo verificado — costó una ronda entera de
/// diagnóstico con un usuario real que reportó "el mensaje siempre fue el mismo, nunca cambió"
/// (tenía razón: el mensaje invariante tapaba el bug real, un timeout de construcción de
/// cadena de certificados con las CA "Generation Y" de Let's Encrypt).
/// </summary>
public enum ResultadoPruebaConexion
{
    /// <summary>HTTP 200 y el cuerpo es el JSON esperado de GET / de StockApp.Api (status: "ok").</summary>
    Ok,

    /// <summary>Hubo respuesta HTTP, pero no es la API de Gestión Municipal (otro status, otro puerto ocupado por otra cosa).</summary>
    RespondeOtraCosa,

    /// <summary>El nombre no resuelve (SocketException.HostNotFound): typo en el hostname, o DNS mal configurado.</summary>
    NoResuelveNombre,

    /// <summary>
    /// Hay ruta hacia el host pero nadie responde en ese puerto (conexión rechazada, host
    /// inalcanzable, u otra falla de socket) — o la dirección ingresada no es una URL válida.
    /// </summary>
    NoHayConexion,

    /// <summary>
    /// El servidor respondió al handshake TCP pero el certificado TLS no es válido/confiable
    /// (AuthenticationException dentro de HttpRequestException). NUNCA se resuelve aflojando la
    /// validación — el probador tiene que decirlo, no ocultarlo.
    /// </summary>
    CertificadoInvalido,

    /// <summary>
    /// No hubo respuesta dentro del plazo (HttpClient.Timeout). Distinto de NoHayConexion: acá
    /// sí hubo actividad de red (por ejemplo, Schannel construyendo la cadena de certificados
    /// la primera vez); reintentar suele andar.
    /// </summary>
    Timeout,
}
