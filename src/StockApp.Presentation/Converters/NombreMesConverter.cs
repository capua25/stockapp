using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Número de mes (1 a 12) → nombre con mayúscula inicial ("Marzo"; "Setiembre", como en Uruguay). Lo usan la columna
/// MES del libro caja anual (y su ColumnaAuto) y la sección "Meses con déficit" del PDF anual. Fuera de rango devuelve
/// el número tal cual. Solo de lectura.
/// </summary>
public sealed class NombreMesConverter : IValueConverter
{
    public static readonly NombreMesConverter Instance = new();

    private static readonly string[] Nombres =
    [
        "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
        "Julio", "Agosto", "Setiembre", "Octubre", "Noviembre", "Diciembre",
    ];

    public static string Nombre(int mes) =>
        mes is >= 1 and <= 12 ? Nombres[mes - 1] : mes.ToString(CultureInfo.InvariantCulture);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int mes ? Nombre(mes) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
