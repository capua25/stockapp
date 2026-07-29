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

    [GeneratedRegex(@"(?i)\bPassword\s*=\s*[^;\s""']+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RegexPassword();

    [GeneratedRegex(@"(?i)\bSecret\s*=\s*[^;\s""']+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RegexSecret();

    [GeneratedRegex(@"(?i)\bBearer\s+[^\s""']+", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
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
