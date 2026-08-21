using System;
using Avalonia.Media;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Calculo de contraste WCAG 2.1 (relative luminance + contrast ratio). Umbrales:
/// texto normal AA 4.5:1, AAA 7:1; elementos graficos (iconos, barras, bordes) 3:1.
/// Extraido de ButtonGhostContrasteTests.cs (RatioDeContraste/LuminanciaRelativa) para
/// que SidebarContrasteTests.cs no duplique el mismo calculo.
/// </summary>
public static class Contraste
{
    private static double Canal(byte valor)
    {
        var c = valor / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Publica (no privada) para que los tests de ORDEN de una escala (ej. EscalaTextoContrasteTests)
    /// puedan comparar "mas oscuro que" sin duplicar la formula. Ratio es simetrico y no alcanza para
    /// eso: A vs B da lo mismo que B vs A, no dice cual de los dos es el oscuro.
    /// </summary>
    public static double Luminancia(Color color)
        => 0.2126 * Canal(color.R) + 0.7152 * Canal(color.G) + 0.0722 * Canal(color.B);

    public static double Ratio(Color a, Color b)
    {
        var la = Luminancia(a);
        var lb = Luminancia(b);
        var claro = Math.Max(la, lb);
        var oscuro = Math.Min(la, lb);
        return (claro + 0.05) / (oscuro + 0.05);
    }
}
