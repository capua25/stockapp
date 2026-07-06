using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace StockApp.Presentation.Helpers;

/// <summary>
/// Normaliza fechas escritas sin separadores (ej. "25091999") al valor
/// <see cref="DateTime"/> equivalente, para los <c>CalendarDatePicker</c> de
/// los reportes que usan formato "dd/MM/yyyy". Avalonia 12 parsea el texto
/// tipeado con <see cref="DateTime.ParseExact(string, string, IFormatProvider)"/>
/// de forma estricta, así que "25091999" (sin barras) no matchea y dispara el
/// evento <c>DateValidationError</c> del control. Este helper detecta ese caso
/// puntual (exactamente 8 dígitos, interpretados como "ddMMyyyy") para poder
/// recuperar la fecha desde el handler de ese evento.
/// </summary>
public static class NormalizadorFechaHelper
{
    private static readonly Regex OchoDigitos = new(@"^\d{8}$", RegexOptions.Compiled);

    /// <summary>
    /// Intenta interpretar <paramref name="entrada"/> como 8 dígitos "ddMMyyyy".
    /// Devuelve <c>false</c> para cualquier otro formato (con separadores,
    /// longitud distinta, no numérico) o si los 8 dígitos no forman una fecha
    /// válida (ej. "99999999").
    /// </summary>
    /// <param name="entrada">Texto tipeado por el usuario en el CalendarDatePicker.</param>
    /// <param name="fecha">Fecha resultante si la conversión tuvo éxito.</param>
    /// <returns><c>true</c> si <paramref name="entrada"/> pudo normalizarse a una fecha válida.</returns>
    public static bool TryNormalizarFecha(string? entrada, out DateTime fecha)
    {
        fecha = default;

        if (entrada is null || !OchoDigitos.IsMatch(entrada))
            return false;

        return DateTime.TryParseExact(
            entrada,
            "ddMMyyyy",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out fecha);
    }
}
