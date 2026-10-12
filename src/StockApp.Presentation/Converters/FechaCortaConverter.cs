using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Fecha sin hora ("05/11/2026") con barras fijas: un StringFormat "dd/MM/yyyy" usa el separador de la
/// cultura de la máquina. Null (factura sin vencimiento) → "—". También es el conversor de ColumnaAuto en las columnas de fecha.
/// </summary>
public sealed class FechaCortaConverter : IValueConverter
{
    public static readonly FechaCortaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Formatear(value as DateOnly?);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Formatear(DateOnly? fecha)
        => fecha?.ToString("dd'/'MM'/'yyyy", CultureInfo.InvariantCulture) ?? "—";
}
