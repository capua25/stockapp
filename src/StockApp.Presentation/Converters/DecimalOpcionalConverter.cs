using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using StockApp.Domain.Formato;

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
///
/// Formato único es-UY de <see cref="FormatoEsUy"/> (decisión 2026-10-01; NO se usa la
/// <paramref name="culture"/> que pasa el binding): se muestra "1.500,5" y se parsea con el
/// parseo SEGURO, que rechaza "5.4"/"850.50" con un mensaje que sugiere la forma correcta en
/// vez de leer el punto como miles. Historia: "850,50" llegó a guardarse como 85050 cuando el
/// parseo dependía de la cultura del binding con <see cref="NumberStyles.Number"/>.
/// </summary>
public sealed class DecimalOpcionalConverter : IValueConverter
{
    public static readonly DecimalOpcionalConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? FormatoEsUy.Cantidad(d) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var texto = value as string;
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        if (FormatoEsUy.TryParseDecimal(texto, out var resultado, out var error))
            return resultado;

        return new BindingNotification(
            new EntradaNumericaInvalidaException(error!),
            BindingErrorType.Error);
    }
}
