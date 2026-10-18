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

    /// <summary>Zona a la que se pasa el instante: la de la máquina. Solo las pruebas de vista la fijan (Uruguay) para que el
    /// texto esperado no dependa de dónde corra la suite.</summary>
    internal static TimeZoneInfo Zona { get; set; } = TimeZoneInfo.Local;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTime fecha
            ? TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(fecha, DateTimeKind.Utc), Zona).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
