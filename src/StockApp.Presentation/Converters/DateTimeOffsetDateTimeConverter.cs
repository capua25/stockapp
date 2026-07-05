using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte entre <see cref="DateTimeOffset"/>? (lo que expone
/// <see cref="Avalonia.Controls.DatePicker.SelectedDate"/>) y <see cref="DateTime"/>?
/// (lo que exponen los ViewModels de reportes). Sin este converter, bindear
/// <c>SelectedDate</c> directo a una propiedad <c>DateTime?</c> lanza
/// <see cref="InvalidCastException"/> en runtime.
/// Se usa <c>.DateTime</c> (descarta el offset horario) porque las fechas de
/// reporte son a nivel día, no interesa la zona horaria.
/// </summary>
public sealed class DateTimeOffsetDateTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTime dt ? new DateTimeOffset(dt) : (DateTimeOffset?)null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTimeOffset dto ? dto.DateTime : (DateTime?)null;
}
