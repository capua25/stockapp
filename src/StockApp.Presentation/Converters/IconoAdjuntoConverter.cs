using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>Ícono MDI por tipo de adjunto: PDF o imagen (los únicos que acepta el servidor), y uno genérico por las dudas.</summary>
public sealed class IconoAdjuntoConverter : IValueConverter
{
    public static readonly IconoAdjuntoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Icono(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Icono(string? contentType) => contentType switch
    {
        "application/pdf" => "mdi-file-pdf-box",
        { } t when t.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "mdi-file-image-outline",
        _ => "mdi-file-outline",
    };
}
