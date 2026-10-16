using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Estado calculado de un gasto (el texto de <see cref="EstadoGasto"/>) → tono de su Insignia: Vencida peligro, Parcial
/// advertencia, Pagada éxito; Pendiente y Anulada neutras (la fila anulada además va en texto terciario).
/// </summary>
public sealed class VarianteEstadoGastoConverter : IValueConverter
{
    public static readonly VarianteEstadoGastoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        nameof(EstadoGasto.Vencida) => VarianteInsignia.Peligro,
        nameof(EstadoGasto.Parcial) => VarianteInsignia.Advertencia,
        nameof(EstadoGasto.Pagada) => VarianteInsignia.Exito,
        _ => VarianteInsignia.Neutro,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
