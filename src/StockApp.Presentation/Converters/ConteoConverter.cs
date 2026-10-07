using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Pie de conteo de un listado v2 (spec §5.1): un int a "1 producto" / "12 productos" / "1.250 productos".
/// ConverterParameter = "singular|plural"; la cantidad sale de FormatoEsUy (miles con punto).
/// Se bindea a Items.Count, no a la colección: ObservableCollection notifica Count en cada Add/Clear,
/// mientras que un binding a la colección entera no se re-evalúa cuando cambia su contenido.
/// EsCero / HayElementos: visibilidad del estado vacío, de la grilla y del pie con el mismo binding.
/// </summary>
public sealed class ConteoConverter : IValueConverter
{
    public static readonly ConteoConverter Instance = new();

    public static readonly FuncValueConverter<int, bool> EsCero = new(n => n == 0);

    public static readonly FuncValueConverter<int, bool> HayElementos = new(n => n > 0);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int n || parameter is not string formas || formas.Split('|') is not [var singular, var plural])
            return string.Empty;

        return $"{FormatoEsUy.Cantidad(n)} {(n == 1 ? singular : plural)}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
