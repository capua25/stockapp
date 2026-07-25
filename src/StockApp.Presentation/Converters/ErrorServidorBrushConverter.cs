using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Resalta con un borde visible la fila que tiene un error de servidor (F5d Entrega 2, review
/// final Important I3): TieneErrorServidor (FilaImportacionEditableVmBase) es INDEPENDIENTE de
/// EstadoFila — antes de este fix el 400 sólo se veía en el tooltip de la fila (ToolTip.Tip
/// bindeado a MensajeErrorServidor en el Style de DataGridRow), sin ninguna señal visual, aunque
/// el diálogo de error decía "revisá las filas resaltadas". Mismo criterio de fallback que
/// EstadoFilaBrushConverter: sin Application.Current se usa el espejo hardcodeado del token.
/// </summary>
public sealed class ErrorServidorBrushConverter : IValueConverter
{
    public static readonly ErrorServidorBrushConverter Instance = new();

    private const string Token = "DangerBrush";
    private static readonly IBrush Fallback = new ImmutableSolidColorBrush(Color.Parse("#DC2626"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true) return Brushes.Transparent;

        if (AvaloniaApp.Current is { } app && app.TryFindResource(Token, out var recurso) && recurso is IBrush brush)
            return brush;

        return Fallback;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
