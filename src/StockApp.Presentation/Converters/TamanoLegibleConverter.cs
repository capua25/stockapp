using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Tamaño de un adjunto para leer de un vistazo, en es-UY: "850 B", "178 KB", "1,2 MB". KB sin decimales; MB con hasta
/// uno. El separador decimal es el de es-UY sin importar la cultura de la máquina.
/// </summary>
public sealed class TamanoLegibleConverter : IValueConverter
{
    public static readonly TamanoLegibleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long bytes ? Formatear(bytes) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Formatear(long bytes)
    {
        if (bytes < 1024)
            return bytes.ToString("0", FormatoEsUy.Formato) + " B";

        var kb = Math.Round(bytes / 1024m, 0, MidpointRounding.AwayFromZero);
        if (kb < 1024)
            return kb.ToString("0", FormatoEsUy.Formato) + " KB";

        return (bytes / (1024m * 1024m)).ToString("0.#", FormatoEsUy.Formato) + " MB";
    }
}
