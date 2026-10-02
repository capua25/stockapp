using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte una cantidad de stock o conteo (<c>decimal</c>/<c>decimal?</c>/<c>int</c>) a
/// texto SIN ceros de relleno y con punto de miles (22m → "22", 22.5m → "22,5", 1500m →
/// "1.500"), con el formato único es-UY de <see cref="FormatoEsUy"/> (decisión 2026-10-01). Soporta
/// <c>int</c> además de <c>decimal</c> porque columnas de conteo (ej. <c>CantidadMovimientos</c>,
/// <c>CantidadProductos</c>) son enteras en el DTO pero comparten el mismo criterio de formateo.
///
/// Expuesto como instancia estática, igual que <see cref="MonedaConverter"/>. Solo de
/// LECTURA (grilla de solo lectura de Valorización): <see cref="ConvertBack"/> no está
/// soportado.
/// </summary>
public sealed class CantidadConverter : IValueConverter
{
    public static readonly CantidadConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            decimal d => FormatoEsUy.Cantidad(d),
            int i => FormatoEsUy.Cantidad(i),
            null => string.Empty,
            _ => string.Empty,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
