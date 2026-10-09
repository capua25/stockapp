using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Monto → texto para una columna de precio que no siempre aplica (PRECIO del Historial de movimientos): 0 → "—" (salida o
/// ajuste sin precio); cualquier otro <c>decimal</c> → <see cref="FormatoEsUy.Moneda"/>, igual que
/// <see cref="MonedaConverter"/>. null → "". Solo de LECTURA: <see cref="ConvertBack"/> lanza.
/// </summary>
public sealed class MonedaOGuionConverter : IValueConverter
{
    public static readonly MonedaOGuionConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            0m => "—",
            decimal d => FormatoEsUy.Moneda(d),
            _ => string.Empty,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
