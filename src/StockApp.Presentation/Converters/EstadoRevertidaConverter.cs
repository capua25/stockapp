using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>Texto de la columna Estado del historial: "Activa"/"Revertida".</summary>
public sealed class EstadoRevertidaConverter : IValueConverter
{
    public static readonly EstadoRevertidaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "Revertida" : "Activa";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
