using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Converter a <c>bool</c> del signo de un <c>decimal</c>: negativo -&gt; <c>true</c>, cero o
/// positivo -&gt; <c>false</c>. Gatea la visibilidad del <see cref="Controls.BadgeEstado"/> que
/// acompaña a <see cref="SignoNegativoBrushConverter"/> en los sitios de stock/saldo negativo
/// (Task B2-T, Ruling B-6, 2026-08-19): <see cref="SignoNegativoBrushConverter"/> devuelve un
/// <c>IBrush</c>, no un <c>bool</c>, así que no sirve directamente para <c>IsVisible</c>.
/// Expuesto como instancia estática, igual que <see cref="SignoNegativoBrushConverter"/> y
/// <see cref="ActivoOpacidadConverter"/>. Solo de LECTURA: <see cref="ConvertBack"/> no está
/// soportado.
/// </summary>
public sealed class EsNegativoConverter : IValueConverter
{
    public static readonly EsNegativoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d && d < 0m;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
