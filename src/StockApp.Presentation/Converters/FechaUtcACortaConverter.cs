using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Fecha persistida en UTC a "dd/MM/yyyy" de la fecha LOCAL de la máquina, con barras fijas. Compone
/// <see cref="FechaUtcALocalConverter"/> y <see cref="FechaCortaConverter"/>: este último solo formatea
/// <see cref="DateOnly"/> y devuelve "—" en silencio ante cualquier otro tipo, así que el paso por DateOnly
/// vive acá y no en cada vista.
/// </summary>
public sealed class FechaUtcACortaConverter : IValueConverter
{
    public static readonly FechaUtcACortaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTime fecha
            ? FechaCortaConverter.Formatear(DateOnly.FromDateTime(DateTime.SpecifyKind(fecha, DateTimeKind.Utc).ToLocalTime()))
            : FechaCortaConverter.Formatear(null);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
