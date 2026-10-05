using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Muestra un <see cref="MotivoMovimiento"/> con su texto visible ("Uso o consumo", no
/// "UsoOConsumo") reutilizando <see cref="MotivoMovimientoExtensions.Nombre"/>, la única fuente
/// del texto. Solo de LECTURA (item del combo; el valor seleccionado sigue siendo el enum).
/// </summary>
public sealed class MotivoMovimientoConverter : IValueConverter
{
    public static readonly MotivoMovimientoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is MotivoMovimiento motivo ? motivo.Nombre() : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
