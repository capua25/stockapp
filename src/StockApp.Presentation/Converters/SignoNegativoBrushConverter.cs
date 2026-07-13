using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Resalta en rojo un valor <c>decimal</c> negativo (ej. <c>StockActual</c> en la grilla de
/// Valorización, cuando un producto quedó con stock negativo). Para valores &gt;= 0 devuelve
/// <see cref="AvaloniaProperty.UnsetValue"/> para heredar el foreground default de la celda
/// en vez de fijar un color explícito.
///
/// El rojo usado (#DC2626) es el mismo semántico que <c>DangerBrush</c> en
/// <c>Themes/Tokens.axaml</c>, pero hardcodeado acá en vez de resuelto vía
/// <c>DynamicResource</c>/<c>Application.Current.FindResource</c>: los converters de este
/// proyecto se instancian y testean sin bootstrapear una <c>Application</c> de Avalonia
/// (no hay infraestructura Headless en StockApp.Presentation.Tests), así que depender de
/// recursos de Application rompería en tests. Si en el futuro se agrega esa infraestructura,
/// se puede migrar a <c>DynamicResource</c> para no duplicar el valor.
///
/// Expuesto como instancia estática, igual que <see cref="MonedaConverter"/> y
/// <see cref="CantidadConverter"/>. Solo de LECTURA: <see cref="ConvertBack"/> no está
/// soportado.
/// </summary>
public sealed class SignoNegativoBrushConverter : IValueConverter
{
    public static readonly SignoNegativoBrushConverter Instance = new();

    private static readonly IBrush BrushNegativo = new ImmutableSolidColorBrush(Color.Parse("#DC2626"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d && d < 0 ? BrushNegativo : AvaloniaProperty.UnsetValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
