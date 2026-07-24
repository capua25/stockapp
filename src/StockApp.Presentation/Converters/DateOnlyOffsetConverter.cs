using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte entre <c>DateOnly?</c> (tipo real de Fecha/FechaVencimiento en los DTOs de análisis
/// de Finanzas) y <c>DateTimeOffset?</c> (tipo que bindean CalendarDatePicker/DatePicker de
/// Avalonia 12 — no soportan DateOnly? nativo). Offset SIEMPRE cero: no hay componente de hora
/// en ningún lado del dominio de Finanzas, así que fijar TimeSpan.Zero evita que el offset local
/// de la máquina corra la fecha calendario un día para adelante/atrás al ida-y-vuelta.
/// </summary>
public sealed class DateOnlyOffsetConverter : IValueConverter
{
    public static readonly DateOnlyOffsetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateOnly fecha
            ? new DateTimeOffset(fecha.Year, fecha.Month, fecha.Day, 0, 0, 0, TimeSpan.Zero)
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTimeOffset dto
            ? DateOnly.FromDateTime(dto.DateTime)
            : null;
}
