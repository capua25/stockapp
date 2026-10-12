using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.Converters;

/// <summary>Porcentaje de ejecución en es-UY con dos decimales: 47.3 → "47,30 %". El valor ya viene en escala 0-100.</summary>
public sealed class PorcentajeConverter : IValueConverter
{
    public static readonly PorcentajeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? Formatear(d) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Formatear(decimal porcentaje) => FormatoEsUy.Decimal(porcentaje) + " %";
}
