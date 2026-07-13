using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte una cantidad de stock o conteo (<c>decimal</c>/<c>decimal?</c>/<c>int</c>) a
/// texto SIN ceros de relleno (formato "0.####": 22m → "22", 22.5m → "22,5", 22.0000m → "22"),
/// con coma decimal fija es-UY — mismo criterio de cultura fija que <see cref="MonedaConverter"/>
/// (ver ahí el porqué de no depender de la cultura del hilo/entorno, y el fallback manual
/// si "es-UY" no está disponible en el runtime). Soporta <c>int</c> además de <c>decimal</c>
/// porque columnas de conteo (ej. <c>CantidadMovimientos</c>, <c>CantidadProductos</c>) son
/// enteras en el DTO pero comparten el mismo criterio de formateo que las cantidades decimales.
///
/// Expuesto como instancia estática, igual que <see cref="MonedaConverter"/>. Solo de
/// LECTURA (grilla de solo lectura de Valorización): <see cref="ConvertBack"/> no está
/// soportado.
/// </summary>
public sealed class CantidadConverter : IValueConverter
{
    public static readonly CantidadConverter Instance = new();

    private const string FormatoSinRelleno = "0.####";

    private static readonly IFormatProvider FormatoNumero = CrearFormatoNumero();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            decimal d => d.ToString(FormatoSinRelleno, FormatoNumero),
            int i => ((decimal)i).ToString(FormatoSinRelleno, FormatoNumero),
            null => string.Empty,
            _ => string.Empty,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static IFormatProvider CrearFormatoNumero()
    {
        try
        {
            return CultureInfo.GetCultureInfo("es-UY");
        }
        catch (CultureNotFoundException)
        {
            return new NumberFormatInfo
            {
                NumberDecimalSeparator = ",",
                NumberGroupSeparator = ".",
            };
        }
    }
}
