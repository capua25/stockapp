using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using StockApp.Application.Finanzas;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Colorea la fila de las grillas del Paso 2 del wizard (F5d §5) por EstadoFila: rojo Error,
/// amarillo Advertencia, sin color (hereda el fondo) para Ok. Mismo criterio de fallback que
/// SignoNegativoBrushConverter: sin Application.Current (StockApp.Presentation.Tests no tiene
/// infraestructura Avalonia Headless) se usa el espejo hardcodeado del token.
/// </summary>
public sealed class EstadoFilaBrushConverter : IValueConverter
{
    public static readonly EstadoFilaBrushConverter Instance = new();

    private const string TokenError = "DangerBrush";
    private const string TokenAdvertencia = "WarningBrush";

    private static readonly IBrush FallbackError = new ImmutableSolidColorBrush(Color.Parse("#DC2626"));
    private static readonly IBrush FallbackAdvertencia = new ImmutableSolidColorBrush(Color.Parse("#D97706"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EstadoFila estado || estado == EstadoFila.Ok)
            return AvaloniaProperty.UnsetValue;

        var token = estado == EstadoFila.Error ? TokenError : TokenAdvertencia;
        var fallback = estado == EstadoFila.Error ? FallbackError : FallbackAdvertencia;

        if (AvaloniaApp.Current is { } app && app.TryFindResource(token, out var recurso) && recurso is IBrush brush)
            return brush;

        return fallback;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
