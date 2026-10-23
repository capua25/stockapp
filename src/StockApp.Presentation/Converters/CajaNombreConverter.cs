using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.Converters;

/// <summary>Caja → su nombre visible ("Local" / "POA"). Solo ida.</summary>
public sealed class CajaNombreConverter : IValueConverter
{
    public static readonly CajaNombreConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Caja caja ? caja.Nombre() : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
