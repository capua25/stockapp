using System;
using Avalonia;
using Avalonia.Controls;
using StockApp.Domain.Formato;
using StockApp.Presentation.Converters;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Attached behavior para un <see cref="TextBox"/> numérico: cuando el binding falla, muestra el
/// mensaje CLARO de <see cref="FormatoEsUy.TryParseDecimal"/> (ej. "Usá coma para los decimales:
/// 5,4") en vez del genérico "Ingresá un número válido.".
///
/// Por qué hace falta (verificado decompilando Avalonia.Base 12.0.5): el error que devuelve un
/// converter en <c>ConvertBack</c> no llega a <c>DataValidationErrors</c>; Avalonia publica su
/// propio <see cref="InvalidCastException"/> y el <see cref="ErrorValidacionConverter"/> global
/// (que no conoce el control) lo reemplaza por el genérico. Este behavior pone un ErrorConverter
/// LOCAL que sí conoce el <see cref="TextBox"/>: re-parsea su texto y, si FormatoEsUy tiene un
/// mensaje específico, lo muestra; si no, delega en el converter global.
///
/// Se activa con <c>beh:EntradaNumericaBehavior.EsDecimal="True"</c> en el XAML del TextBox.
/// </summary>
public static class EntradaNumericaBehavior
{
    public static readonly AttachedProperty<bool> EsDecimalProperty =
        AvaloniaProperty.RegisterAttached<TextBox, TextBox, bool>("EsDecimal");

    static EntradaNumericaBehavior()
    {
        EsDecimalProperty.Changed.AddClassHandler<TextBox>(OnEsDecimalChanged);
    }

    public static bool GetEsDecimal(TextBox caja) => caja.GetValue(EsDecimalProperty);

    public static void SetEsDecimal(TextBox caja, bool value) => caja.SetValue(EsDecimalProperty, value);

    private static void OnEsDecimalChanged(TextBox caja, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.GetNewValue<bool>())
            DataValidationErrors.SetErrorConverter(caja, error => Convertir(caja, error));
        else
            caja.ClearValue(DataValidationErrors.ErrorConverterProperty);
    }

    private static object Convertir(TextBox caja, object error)
    {
        if (error is Exception
            && !string.IsNullOrWhiteSpace(caja.Text)
            && !FormatoEsUy.TryParseDecimal(caja.Text, out _, out var mensaje)
            && mensaje is not null
            && mensaje != FormatoEsUy.MensajeNoEsNumero)
            return mensaje;

        return ErrorValidacionConverter.Instance(error);
    }
}
