using System.Text.RegularExpressions;

namespace StockApp.Api.Logging;

/// <summary>
/// Enmascara credenciales en el texto ya renderizado de un evento de log, antes de que
/// toque el disco. Se sanea el texto final —y no las propiedades del evento— porque la
/// connection string con la contraseña suele viajar dentro del stack trace de una
/// excepción de Npgsql, y <c>LogEvent.Exception</c> no es modificable por un enricher.
/// El ZIP de logs termina en la máquina de un administrativo y probablemente adjunto en
/// un mail: acá no se filtra nada.
/// </summary>
internal static partial class SaneadorCredenciales
{
    private const string Mascara = "***";

    // El valor puede venir entre comillas (dobles o simples, con ; adentro), entre comillas
    // SIN cerrar (la connection string quedó cortada a la mitad) o sin comillas. El caso sin
    // comillas tiene DOS variantes que hay que distinguir para no romper ninguna de las dos:
    //   - connection string real: "Password=hola mundo;Host=x" -> el valor tiene espacios
    //     adentro y termina en el próximo ';' (Npgsql lo permite sin comillas).
    //   - mensaje de log libre: "Secret=clave-abc y sigue el mensaje" -> no hay ';' que
    //     delimite el valor, así que hay que cortar en el primer espacio como antes, o el
    //     saneador se comería el resto del mensaje.
    // El lookahead (?=;) en la primera rama sin comillas hace exactamente esa distinción: solo
    // se anima a incluir espacios si más adelante en la línea hay un ';' que cierre el valor;
    // si no lo hay, esa rama nunca matchea y cae a la de siempre (corta en el primer espacio).
    // Pwd/Psw son alias reales de Password para Npgsql (aprobado por el dueño del proyecto):
    // sin ellos, una connection string armada con "Pwd=" queda completamente sin sanear.
    [GeneratedRegex(@"(?i)\b(?:Password|Pwd|Psw)\s*=\s*(?:""[^""\r\n]*""?|'[^'\r\n]*'?|[^;\r\n]*(?=;)|[^;\s\r\n]+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RegexPassword();

    [GeneratedRegex(@"(?i)\bSecret\s*=\s*(?:""[^""\r\n]*""?|'[^'\r\n]*'?|[^;\r\n]*(?=;)|[^;\s\r\n]+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RegexSecret();

    // Bearer sí corta en whitespace en la rama sin comillas: un JWT no tiene espacios, y
    // consumir hasta fin de línea acá se comería el resto del mensaje de log.
    [GeneratedRegex(@"(?i)\bBearer\s+(?:""[^""\r\n]*""?|'[^'\r\n]*'?|[^;\s""']+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RegexBearer();

    internal static string Sanear(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return texto;

        var resultado = RegexPassword().Replace(texto, $"Password={Mascara}");
        resultado = RegexSecret().Replace(resultado, $"Secret={Mascara}");
        resultado = RegexBearer().Replace(resultado, $"Bearer {Mascara}");
        return resultado;
    }
}
