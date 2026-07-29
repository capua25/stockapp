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
    // SIN cerrar (la connection string quedó cortada a la mitad) o sin comillas. Sin comillas,
    // Npgsql permite espacios adentro del valor (p.ej. "Password=hola mundo;Host=x"), así que
    // la rama final es simplemente "todo lo que no sea ; ni fin de línea": [^;\r\n]* para en el
    // próximo ';' si lo hay, y si no lo hay llega hasta el final de la línea sin backtracking
    // (el motor de regex la resuelve de un solo paso, es lineal).
    //
    // ¡OJO! Antes esta rama tenía un lookahead (?=;) para "solo animarse a incluir espacios si
    // más adelante hay un ';'" y así no comerse mensajes de log libres sin ';'. Eso era CUADRÁTICO:
    // sin ';' en el resto de la línea, el motor consume codicioso hasta el final, falla el
    // lookahead, y retrocede carácter por carácter — por cada "Password=" en el texto. Medido:
    // con matchTimeoutMilliseconds: 1000, una línea de ~230.000 caracteres sin ';' ya tira
    // RegexMatchTimeoutException, y como Sanear() no tiene try/catch y Serilog se traga la
    // excepción del Emit en silencio, ESE EVENTO DE LOG SE PIERDE COMPLETO sin dejar rastro.
    // La solución no es acotar el lookahead: es no usar lookahead. [^;\r\n]* solo, sin la rama
    // alternativa de whitespace-cut, cubre los tres casos (con ';', sin ';', vacío) en un paso.
    // Pwd/Psw son alias reales de Password para Npgsql (aprobado por el dueño del proyecto):
    // sin ellos, una connection string armada con "Pwd=" queda completamente sin sanear.
    [GeneratedRegex(@"(?i)\b(?:Password|Pwd|Psw)\s*=\s*(?:""[^""\r\n]*""?|'[^'\r\n]*'?|[^;\r\n]*)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RegexPassword();

    [GeneratedRegex(@"(?i)\bSecret\s*=\s*(?:""[^""\r\n]*""?|'[^'\r\n]*'?|[^;\r\n]*)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
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
