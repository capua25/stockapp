using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StockApp.UiV2.Herramientas.Tokens;

/// <summary>
/// Genera las dos salidas de tokens.json (spec §3.2): recursos de Avalonia y variables CSS de
/// los mockups. Salida determinista: mismo JSON, mismos bytes (LF, sin BOM, orden del JSON).
/// Ningún texto emitido puede contener "--" dentro del .axaml (AVLN1001).
/// </summary>
public static partial class GeneradorTokens
{
    private const string EncabezadoAxaml =
        "<!-- ARCHIVO GENERADO. No editar a mano.\n" +
        "     Fuente: docs/ui-v2/tokens.json. Se regenera con el comando \"tokens\" de\n" +
        "     tools/StockApp.UiV2.Herramientas (ver docs/ui-v2/herramientas.md). -->\n";

    private const string EncabezadoCss =
        "/* ARCHIVO GENERADO. No editar a mano. Fuente: docs/ui-v2/tokens.json\n" +
        "   (comando \"tokens\" de tools/StockApp.UiV2.Herramientas). */\n";

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex LimiteDePalabra();

    internal static string AKebab(string pascal) => LimiteDePalabra().Replace(pascal, "-$1").ToLowerInvariant();

    internal static byte AlfaDe(double opacidad) =>
        (byte)Math.Round(opacidad * 255, MidpointRounding.AwayFromZero);

    private static string N(double valor) => valor.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>
    /// Alto de línea que el mockup debe usar para un rol: el fijado por el token o, si no hay,
    /// el natural de Avalonia (alto de la fuente redondeado hacia arriba por el layout rounding).
    /// En el .axaml no se emite: Avalonia lo calcula solo.
    /// </summary>
    public static double AltoLineaCss(EstiloTipografico estilo, double factorAltoLinea) =>
        estilo.AltoLinea ?? Math.Ceiling(estilo.Tamano * factorAltoLinea);

    public static string GenerarAxaml(DefinicionTokens d)
    {
        var p = d.Prefijo;
        var sb = new StringBuilder();
        sb.Append(EncabezadoAxaml);
        sb.Append("<ResourceDictionary xmlns=\"https://github.com/avaloniaui\"\n");
        sb.Append("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n");

        sb.Append("\n    <!-- Colores -->\n");
        foreach (var (nombre, hex) in d.Colores)
        {
            sb.Append($"    <Color x:Key=\"{p}Color{nombre}\">{hex}</Color>\n");
        }

        sb.Append("\n    <!-- Brushes: lo que referencian las vistas y los ControlTheme -->\n");
        foreach (var (nombre, hex) in d.Colores)
        {
            sb.Append($"    <SolidColorBrush x:Key=\"{p}{nombre}Brush\" Color=\"{hex}\" />\n");
        }

        sb.Append("\n    <!-- Radios -->\n");
        foreach (var (nombre, valor) in d.Radios)
        {
            sb.Append($"    <CornerRadius x:Key=\"{p}Radio{nombre}\">{N(valor)}</CornerRadius>\n");
        }

        sb.Append("\n    <!-- Alturas -->\n");
        foreach (var (nombre, valor) in d.Alturas)
        {
            sb.Append($"    <x:Double x:Key=\"{p}Alto{nombre}\">{N(valor)}</x:Double>\n");
        }

        sb.Append("\n    <!-- Anchos -->\n");
        foreach (var (nombre, valor) in d.Anchos)
        {
            sb.Append($"    <x:Double x:Key=\"{p}Ancho{nombre}\">{N(valor)}</x:Double>\n");
        }

        sb.Append("\n    <!-- Espaciado, base 4 -->\n");
        foreach (var valor in d.Espaciado)
        {
            sb.Append($"    <x:Double x:Key=\"{p}Espacio{N(valor)}\">{N(valor)}</x:Double>\n");
        }

        var s = d.SombraCard;
        sb.Append("\n    <!-- Sombra: la única del sistema -->\n");
        sb.Append($"    <BoxShadows x:Key=\"{p}SombraCard\">{N(s.X)} {N(s.Y)} {N(s.Desenfoque)} {N(s.Expansion)} ");
        sb.Append($"#{AlfaDe(s.Opacidad):X2}{s.Color[1..]}</BoxShadows>\n");

        sb.Append("\n    <!-- Tipografía -->\n");
        foreach (var (nombre, t) in d.Tipografia)
        {
            sb.Append($"    <x:Double x:Key=\"{p}Fuente{nombre}Tamano\">{N(t.Tamano)}</x:Double>\n");
            sb.Append($"    <FontWeight x:Key=\"{p}Fuente{nombre}Peso\">{t.Peso}</FontWeight>\n");
            if (t.AltoLinea is { } alto)
            {
                sb.Append($"    <x:Double x:Key=\"{p}Fuente{nombre}AltoLinea\">{N(alto)}</x:Double>\n");
            }

            if (t.EspaciadoLetras is { } espaciado)
            {
                sb.Append($"    <x:Double x:Key=\"{p}Fuente{nombre}EspaciadoLetras\">{N(espaciado)}</x:Double>\n");
            }
        }

        sb.Append("\n</ResourceDictionary>\n");
        return sb.ToString();
    }

    public static string GenerarCss(DefinicionTokens d)
    {
        var p = d.Prefijo.Length == 0 ? "--" : $"--{AKebab(d.Prefijo)}-";
        var sb = new StringBuilder();
        sb.Append(EncabezadoCss);
        sb.Append(":root {\n");

        foreach (var (nombre, hex) in d.Colores)
        {
            sb.Append($"  {p}color-{AKebab(nombre)}: {hex};\n");
        }

        foreach (var (nombre, valor) in d.Radios)
        {
            sb.Append($"  {p}radio-{AKebab(nombre)}: {N(valor)}px;\n");
        }

        foreach (var (nombre, valor) in d.Alturas)
        {
            sb.Append($"  {p}alto-{AKebab(nombre)}: {N(valor)}px;\n");
        }

        foreach (var (nombre, valor) in d.Anchos)
        {
            sb.Append($"  {p}ancho-{AKebab(nombre)}: {N(valor)}px;\n");
        }

        foreach (var valor in d.Espaciado)
        {
            sb.Append($"  {p}espacio-{N(valor)}: {N(valor)}px;\n");
        }

        var s = d.SombraCard;
        var r = Convert.ToInt32(s.Color[1..3], 16);
        var g = Convert.ToInt32(s.Color[3..5], 16);
        var b = Convert.ToInt32(s.Color[5..7], 16);
        var alfa = (AlfaDe(s.Opacidad) / 255.0).ToString("0.###", CultureInfo.InvariantCulture);
        sb.Append($"  {p}sombra-card: {N(s.X)}px {N(s.Y)}px {N(s.Desenfoque)}px {N(s.Expansion)}px rgba({r}, {g}, {b}, {alfa});\n");

        foreach (var (nombre, t) in d.Tipografia)
        {
            var k = $"{p}fuente-{AKebab(nombre)}";
            sb.Append($"  {k}-tamano: {N(t.Tamano)}px;\n");
            sb.Append($"  {k}-peso: {t.Peso};\n");
            sb.Append($"  {k}-alto-linea: {N(AltoLineaCss(t, d.FactorAltoLinea))}px;\n");

            if (t.EspaciadoLetras is { } espaciado)
            {
                sb.Append($"  {k}-espaciado-letras: {N(espaciado)}px;\n");
            }

            if (t.Mayusculas)
            {
                sb.Append($"  {k}-transformacion: uppercase;\n");
            }
        }

        sb.Append("}\n");
        return sb.ToString();
    }
}
