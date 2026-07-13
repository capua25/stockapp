using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
// Alias porque StockApp.Application (namespace del proyecto de aplicación) colisiona con la
// clase Avalonia.Application. Mismo patrón que App.axaml.cs, ServicioGuardadoArchivo y
// ConfirmacionService.
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Resalta en rojo un valor <c>decimal</c> negativo (ej. <c>StockActual</c> en la grilla de
/// Valorización, cuando un producto quedó con stock negativo). Para valores &gt;= 0 devuelve
/// <see cref="AvaloniaProperty.UnsetValue"/> para heredar el foreground default de la celda
/// en vez de fijar un color explícito.
///
/// En runtime resuelve el brush del token <c>DangerBrush</c> (<c>Themes/Tokens.axaml</c>) vía
/// <c>Application.Current.TryFindResource</c>. Los converters de este proyecto también se
/// instancian y testean sin bootstrapear una <c>Application</c> de Avalonia (no hay
/// infraestructura Headless en StockApp.Presentation.Tests), así que cuando no hay
/// <c>Application.Current</c> (o el recurso no se resuelve) se cae al <see cref="FallbackDanger"/>,
/// que es el espejo hardcodeado de ese mismo token, para no romper la determinística de los tests.
///
/// Expuesto como instancia estática, igual que <see cref="MonedaConverter"/> y
/// <see cref="CantidadConverter"/>. Solo de LECTURA: <see cref="ConvertBack"/> no está
/// soportado.
/// </summary>
public sealed class SignoNegativoBrushConverter : IValueConverter
{
    public static readonly SignoNegativoBrushConverter Instance = new();

    private const string TokenDanger = "DangerBrush";

    /// <summary>
    /// Espejo del token <c>DangerBrush</c> en <c>Themes/Tokens.axaml</c> (#DC2626), usado solo
    /// cuando no hay <c>Application.Current</c> disponible para resolverlo (ej. en
    /// StockApp.Presentation.Tests, que no tiene infraestructura Avalonia Headless).
    /// </summary>
    private static readonly IBrush FallbackDanger = new ImmutableSolidColorBrush(Color.Parse("#DC2626"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal d || d >= 0)
        {
            return AvaloniaProperty.UnsetValue;
        }

        if (AvaloniaApp.Current is { } app && app.TryFindResource(TokenDanger, out var recurso) && recurso is IBrush brush)
        {
            return brush;
        }

        return FallbackDanger;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
