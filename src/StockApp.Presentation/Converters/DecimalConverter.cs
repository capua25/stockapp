using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte entre un <c>decimal</c> OBLIGATORIO (no nullable) y el <c>string</c> que edita un
/// <c>TextBox</c> o una celda de <c>DataGridTextColumn</c>, con el formato único es-UY de
/// <see cref="FormatoEsUy"/> (decisión 2026-10-01): se muestra "1.500,5" y se parsea con el
/// parseo SEGURO, que rechaza "5.4" con "Usá coma para los decimales: 5,4" en vez de leerlo
/// como 54. Ignora la <paramref name="culture"/> del binding a propósito.
///
/// OJO (verificado decompilando Avalonia.Base 12.0.5, <c>BindingExpression.WriteValueToSource</c>):
/// el <see cref="BindingNotification"/> de error que devuelve <see cref="ConvertBack"/> NO llega
/// como tal a <c>DataValidationErrors</c>; Avalonia intenta castearlo al tipo destino y publica un
/// <see cref="InvalidCastException"/> propio, que <see cref="ErrorValidacionConverter"/> reemplaza
/// por el genérico. El mensaje claro ("Usá coma...") lo pone en pantalla
/// <see cref="StockApp.Presentation.Behaviors.EntradaNumericaBehavior"/>, que hay que activar en
/// el <c>TextBox</c>. El converter igual devuelve el error con el mensaje de dominio (no rompe el
/// pipeline y queda en el log de binding).
///
/// Reemplaza al extinto <c>DecimalPuntoConverter</c> (punto decimal invariante, decisión del
/// 2026-08-24 superada). Cadena vacía es error (el campo es obligatorio); para un
/// <c>decimal?</c> opcional, ver <see cref="DecimalOpcionalConverter"/>.
/// </summary>
public sealed class DecimalConverter : IValueConverter
{
    public static readonly DecimalConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? FormatoEsUy.Cantidad(d) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var texto = value as string;
        if (string.IsNullOrWhiteSpace(texto))
            return Error("El valor ingresado no puede estar vacío.");

        return FormatoEsUy.TryParseDecimal(texto, out var resultado, out var error)
            ? resultado
            : Error(error!);
    }

    private static BindingNotification Error(string mensaje)
        => new(new FormatException(mensaje), BindingErrorType.Error);
}
