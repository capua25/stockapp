using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte entre <c>decimal?</c> y el <c>string</c> que edita un <c>TextBox</c>, tratando
/// cadena vacía/blanco como <c>null</c> (campo opcional sin valor) en vez de dejar que el
/// conversor default de Avalonia intente castear "" a <c>decimal</c> y explote con
/// <see cref="InvalidCastException"/> — bug reproducido en "Precio unitario"
/// (<see cref="StockApp.Presentation.Views.Movimientos.MovimientoFormControl"/>, compartido
/// por Registrar Entrada y Registrar Salida). Si el texto no es un número válido, en vez de
/// lanzar se devuelve un <see cref="BindingNotification"/> en estado de error (patrón oficial
/// de Avalonia para converters: lanzar una excepción real se trata como "excepción de
/// aplicación" y puede interrumpir el pipeline de binding). Expuesto como instancia estática,
/// igual que <see cref="ColeccionVaciaConverter"/>.
/// </summary>
public sealed class DecimalOpcionalConverter : IValueConverter
{
    public static readonly DecimalOpcionalConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? d.ToString(culture) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var texto = value as string;
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        if (decimal.TryParse(texto, NumberStyles.Number, culture, out var resultado))
            return resultado;

        return new BindingNotification(
            new FormatException("El valor ingresado no es un número válido."),
            BindingErrorType.Error);
    }
}
