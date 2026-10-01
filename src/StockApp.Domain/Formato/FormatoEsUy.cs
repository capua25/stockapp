using System.Globalization;
using System.Text.RegularExpressions;

namespace StockApp.Domain.Formato;

/// <summary>
/// ÚNICO punto de verdad del formato numérico de la app (decisión del 2026-10-01, no
/// configurable): es-UY en pantalla, PDF, CSV, mensajes y entrada de datos — coma decimal y
/// punto de miles (ej. <c>$ 26.400,00</c>). El cliente es un municipio de Uruguay.
///
/// Vive en Domain porque es la capa más baja que comparten TODOS los consumidores: Application
/// (CSV, mensajes de servicios), Infrastructure (mensajes de repositorios), Documentos (PDF),
/// Presentation (converters, ViewModels, cultura del arranque) y las excepciones de dominio.
///
/// El <see cref="NumberFormatInfo"/> se arma A MANO: no depende de ICU (la Api corre con
/// <c>InvariantGlobalization</c>, donde <c>GetCultureInfo("es-UY")</c> tira) ni de la cultura del
/// hilo/SO. Un test verifica que, cuando ICU sí tiene es-UY, la salida coincide con ella.
/// </summary>
public static partial class FormatoEsUy
{
    public const string MensajeNoEsNumero = "El valor ingresado no es un número válido.";

    public const string MensajeAgrupacionInvalida =
        "Número mal escrito: usá coma para los decimales y punto solo para separar miles de a tres cifras (ej.: 1.500,50).";

    private const string FormatoCantidad = "#,##0.####";

    /// <summary>Separadores y patrones de es-UY (verificados contra ICU, ver tests), de solo lectura.</summary>
    public static NumberFormatInfo Formato { get; } = NumberFormatInfo.ReadOnly(new NumberFormatInfo
    {
        NegativeSign = "-",
        PositiveSign = "+",
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = [3],
        NumberDecimalDigits = 2,
        NumberNegativePattern = 1,
        CurrencySymbol = "$",
        CurrencyDecimalSeparator = ",",
        CurrencyGroupSeparator = ".",
        CurrencyGroupSizes = [3],
        CurrencyDecimalDigits = 2,
        CurrencyPositivePattern = 2,   // "$ n"
        CurrencyNegativePattern = 9,   // "-$ n"
        PercentDecimalSeparator = ",",
        PercentGroupSeparator = ".",
        PercentGroupSizes = [3],
    });

    /// <summary>Monto en pesos con 2 decimales fijos: 26400m → "$ 26.400,00"; -600m → "-$ 600,00".</summary>
    public static string Moneda(decimal monto) => monto.ToString("C2", Formato);

    /// <summary>Cantidad sin ceros de relleno, con miles: 1500.5000m → "1.500,5"; 22.0000m → "22".</summary>
    public static string Cantidad(decimal cantidad) => cantidad.ToString(FormatoCantidad, Formato);

    /// <summary>Número con 2 decimales fijos y miles, sin símbolo: 26400m → "26.400,00".</summary>
    public static string Decimal(decimal valor) => valor.ToString("N2", Formato);

    /// <summary>
    /// Parseo SEGURO de un número tipeado en es-UY. A diferencia de <c>NumberStyles.Number</c>
    /// con una cultura es-*, que es laxo con los grupos de miles ("5.4" → 54, "1.5" → 15, sin
    /// aviso), acá el punto SOLO vale como separador de miles en grupos exactos de tres cifras.
    /// Acepta: 1500 · 1500,5 · 1.500 · 1.500,50 · -3,25 · 0,5.
    /// Rechaza con mensaje: 5.4 · 1.5 · 12.34 · 1.50,5 · 1,500.50 · vacío · texto.
    /// </summary>
    public static bool TryParseDecimal(string? texto, out decimal valor, out string? error)
    {
        valor = 0m;
        var limpio = texto?.Trim() ?? string.Empty;

        var match = NumeroEsUy().Match(limpio);
        if (match.Success)
        {
            var invariante = match.Groups["signo"].Value
                + match.Groups["entera"].Value.Replace(".", string.Empty)
                + (match.Groups["dec"].Success ? "." + match.Groups["dec"].Value : string.Empty);

            if (decimal.TryParse(invariante, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var resultado))
            {
                valor = resultado;
                error = null;
                return true;
            }

            error = MensajeNoEsNumero;   // overflow
            return false;
        }

        error = MensajePara(limpio);
        return false;
    }

    /// <summary>
    /// Cultura para fijar como default del proceso de la UI: es-UY si el runtime la tiene (por
    /// fechas/nombres), con el <see cref="NumberFormatInfo"/> de ESTE punto de verdad; si no la
    /// tiene, una copia de Invariant con el mismo formato numérico y fecha corta dd/MM/yyyy.
    /// Devuelve una instancia nueva cada vez: modificarla no toca <see cref="Formato"/>.
    /// </summary>
    public static CultureInfo CrearCultura()
    {
        CultureInfo cultura;
        try
        {
            cultura = new CultureInfo("es-UY");
        }
        catch (CultureNotFoundException)
        {
            cultura = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            cultura.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
        }

        cultura.NumberFormat = (NumberFormatInfo)Formato.Clone();
        return cultura;
    }

    private static string MensajePara(string texto)
    {
        if (texto.Length == 0)
            return MensajeNoEsNumero;

        // "5.4", "12.34", "-3.25": punto usado como decimal → se sugiere lo mismo con coma.
        if (PuntoComoDecimal().IsMatch(texto))
            return $"Usá coma para los decimales: {texto.Replace('.', ',')}";

        // "1,500.50": formato inglés completo → se sugiere con los separadores intercambiados.
        if (FormatoIngles().IsMatch(texto))
        {
            var sugerido = texto.Replace('.', '\u0001').Replace(',', '.').Replace('\u0001', ',');
            return $"Usá coma para los decimales y punto para los miles: {sugerido}";
        }

        return texto.Contains('.') ? MensajeAgrupacionInvalida : MensajeNoEsNumero;
    }

    [GeneratedRegex(@"^(?<signo>-)?(?<entera>\d{1,3}(?:\.\d{3})+|\d+)(?:,(?<dec>\d+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumeroEsUy();

    [GeneratedRegex(@"^-?\d+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex PuntoComoDecimal();

    [GeneratedRegex(@"^-?\d{1,3}(?:,\d{3})+\.\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoIngles();
}
