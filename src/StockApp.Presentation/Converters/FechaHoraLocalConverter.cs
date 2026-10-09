using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Instante persistido en UTC → TEXTO "dd/MM/yyyy HH:mm" en hora local (ej. la columna FECHA del Historial v2).
///
/// Como <see cref="FechaUtcALocalConverter"/> fuerza <c>Kind=Utc</c> antes de <c>ToLocalTime()</c> (el
/// <c>DateTime</c> puede volver con <c>Kind=Unspecified</c>), pero además formatea con un formato FIJO e
/// invariante: no depende de la cultura que pase el binding ni de un <c>StringFormat</c> en el XAML.
/// null → "". Solo de LECTURA: <see cref="ConvertBack"/> lanza.
/// </summary>
public sealed class FechaHoraLocalConverter : IValueConverter
{
    public static readonly FechaHoraLocalConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTime fecha
            ? DateTime.SpecifyKind(fecha, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
