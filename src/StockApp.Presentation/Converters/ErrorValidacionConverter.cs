using System;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Función de conversión para <c>DataValidationErrors.ErrorConverter</c> (propiedad adjunta
/// de Avalonia, tipo <c>Func&lt;object,object&gt;</c>, NO <c>IValueConverter</c>). Se aplica a
/// cada error ANTES de que quede expuesto en <c>DataValidationErrors.Errors</c> — ver
/// <c>DataValidationErrors.OnErrorsOrConverterChanged</c> en el código fuente de Avalonia —
/// por lo que reemplazar acá el contenido alcanza para blindar cualquier control (no hace
/// falta tocar el <c>ErrorTemplate</c> ni el <c>Template</c> que decide DÓNDE/CÓMO se muestra).
///
/// Política del proyecto: ninguna excepción cruda de .NET debe llegar a la UI. Cuando el error
/// es una <see cref="Exception"/> (típicamente lanzada por <c>ExceptionValidationPlugin</c> al
/// fallar la conversión de binding, p. ej. texto no numérico en un campo decimal) se reemplaza
/// por un mensaje de dominio genérico. Si en el futuro se agrega validación explícita en el
/// ViewModel (INotifyDataErrorInfo/DataAnnotations) que ya produce mensajes de dominio como
/// <c>string</c>, esos pasan sin modificar.
///
/// Registrado globalmente sobre TextBox/ComboBox en Themes/Controls.axaml vía
/// <c>{x:Static conv:ErrorValidacionConverter.Instance}</c>.
/// </summary>
public static class ErrorValidacionConverter
{
    private const string MensajeGenerico = "Ingresá un número válido.";

    public static readonly Func<object, object> Instance = Convertir;

    private static object Convertir(object error)
        => error is Exception ? MensajeGenerico : error;
}
