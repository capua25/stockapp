using System.Text.RegularExpressions;

namespace StockApp.UiV2.Herramientas.Mockups;

/// <summary>
/// Convenciones de los mockups que el navegador no hace cumplir (docs/ui-v2/herramientas.md).
/// </summary>
public static partial class VerificadorConvenciones
{
    // Un elemento cuya lista de clases tiene v2-num EXACTA (no v2-numero) y su contenido hasta el
    // cierre de la misma etiqueta. Las celdas numéricas no anidan elementos del mismo tipo.
    [GeneratedRegex("""<(?<tag>[a-z][a-z0-9]*)\b[^>]*\bclass="(?:[^"]*\s)?v2-num(?:\s[^"]*)?"[^>]*>(?<interior>.*?)</\k<tag>>""",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex CeldaNumerica();

    [GeneratedRegex("""^\s*<span>[^<]*</span>\s*$""")]
    private static partial Regex UnSoloSpan();

    /// <summary>Elementos con clase <c>v2-num</c> cuyo contenido no es un único &lt;span&gt; de texto.</summary>
    public static IReadOnlyList<string> CeldasNumericasSinSpan(string html) =>
        CeldaNumerica().Matches(html)
            .Where(m => !UnSoloSpan().IsMatch(m.Groups["interior"].Value))
            .Select(m => m.Value.Length > 120 ? m.Value[..120] + "…" : m.Value)
            .ToList();
}
