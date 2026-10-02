using System.Globalization;

namespace StockApp.UiV2.Herramientas;

/// <summary>
/// Tamaño canónico de mockups y renders de la UI v2 (spec §6.1). Es el alias de
/// <see cref="TamanoLienzo.Estandar"/>: se conserva para no romper a los llamadores de la Fase 0.
/// </summary>
public static class Lienzo
{
    public const int Ancho = 1280;
    public const int Alto = 760;
}

/// <summary>
/// Tamaño de la ventana con el que se capturan mockups y se renderizan vistas (spec §6.1/§6.2):
/// toda comparación corre a <see cref="Estandar"/> (1280x760) y a <see cref="Municipio"/>
/// (1366x768, la pantalla de varias PCs del municipio), con el mismo umbral.
/// </summary>
public readonly record struct TamanoLienzo(int Ancho, int Alto)
{
    public static readonly TamanoLienzo Estandar = new(Lienzo.Ancho, Lienzo.Alto);
    public static readonly TamanoLienzo Municipio = new(1366, 768);

    /// <summary>Los únicos tamaños que acepta la herramienta, en el orden en que se listan.</summary>
    public static IReadOnlyList<TamanoLienzo> Conocidos { get; } = [Estandar, Municipio];

    /// <summary>
    /// Sufijo del nombre de pantalla y de su carpeta de salida: vacío en el estándar (la corrida
    /// de la Fase 0 conserva sus nombres) y "@1366x768" en el resto, para no pisarla.
    /// </summary>
    public string SufijoPantalla => this == Estandar ? "" : "@" + this;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Ancho}x{Alto}");

    /// <summary>"1280x760" o "1366x768"; cualquier otro texto se rechaza.</summary>
    public static bool TryParsear(string texto, out TamanoLienzo tamano)
    {
        foreach (var conocido in Conocidos)
        {
            if (string.Equals(texto, conocido.ToString(), StringComparison.Ordinal))
            {
                tamano = conocido;
                return true;
            }
        }

        tamano = default;
        return false;
    }
}
