using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Converter a <c>bool</c> del signo de un <c>decimal</c>: negativo -&gt; <c>true</c>, cero o
/// positivo -&gt; <c>false</c>. Gatea la visibilidad de la <see cref="Controls.V2.Insignia"/> que
/// marca el stock o saldo negativo ("Stock negativo", "Sobreejecutado", "Saldo negativo", "Déficit",
/// "Vencida"): un <c>IBrush</c> no sirve directamente para <c>IsVisible</c>. Expuesto como instancia
/// estática. Solo de LECTURA: <see cref="ConvertBack"/> no está soportado.
/// </summary>
public sealed class EsNegativoConverter : IValueConverter
{
    public static readonly EsNegativoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d && d < 0m;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
