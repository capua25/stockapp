using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Fecha persistida en UTC a "dd/MM/yyyy" de la fecha LOCAL de la máquina, con barras fijas. Compone
/// <see cref="FechaUtcALocalConverter"/> (UTC a hora local) y <see cref="FechaCortaConverter"/>: este último solo
/// formatea <see cref="DateOnly"/> y devuelve "—" en silencio ante cualquier otro tipo, así que el paso por DateOnly
/// vive acá y no en cada vista.
/// </summary>
public sealed class FechaUtcACortaConverter : IValueConverter
{
    public static readonly FechaUtcACortaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => FechaUtcALocalConverter.Instance.Convert(value, typeof(DateTime), null, culture) is DateTime local
            ? FechaCortaConverter.Formatear(DateOnly.FromDateTime(local))
            : FechaCortaConverter.Formatear(null);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
