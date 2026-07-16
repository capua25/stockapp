using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte un monto (<c>decimal</c>/<c>decimal?</c>) a su representación en pesos: símbolo
/// "$", separador de miles "." y coma decimal, con 2 decimales fijos (ej. 26400m →
/// "$ 26.400,00"; negativos anteponen el signo antes del símbolo, ej. -600m → "-$ 600,00").
///
/// Cultura FIJA es-UY (NO se usa la cultura del hilo/entorno): la app no fija ningún
/// <see cref="CultureInfo"/> global (verificado en Program.cs/App.axaml.cs), así que
/// depender de la cultura ambiente haría que el formato cambie según la máquina donde
/// corra. Si "es-UY" no está disponible en el runtime (ICU deshabilitada / ejecución con
/// InvariantGlobalization — el proyecto Api ya lo usa, aunque Presentation hoy no) se cae a
/// un <see cref="NumberFormatInfo"/> armado a mano con los mismos separadores/símbolo, para
/// no depender de que el SO/runtime tenga la cultura instalada.
///
/// Expuesto como instancia estática, igual que <see cref="DecimalOpcionalConverter"/> y
/// <see cref="FechaUtcALocalConverter"/>. Solo de LECTURA (grilla de solo lectura de
/// Valorización): <see cref="ConvertBack"/> no está soportado.
/// </summary>
public sealed class MonedaConverter : IValueConverter
{
    public static readonly MonedaConverter Instance = new();

    private static readonly IFormatProvider FormatoMoneda = CrearFormatoMoneda();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            decimal d => Formatear(d),
            null => string.Empty,
            _ => string.Empty,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    /// <summary>
    /// Formatea un monto con el mismo criterio que las grillas (ej. 850.5000m → "$ 850,50"),
    /// para reutilizar en mensajes de confirmación/error de los ViewModels sin duplicar el
    /// formato es-UY (bug real: esos mensajes mostraban el decimal crudo, ej. "(799.5000)").
    /// </summary>
    public static string Formatear(decimal monto) => monto.ToString("C2", FormatoMoneda);

    private static IFormatProvider CrearFormatoMoneda()
    {
        try
        {
            return CultureInfo.GetCultureInfo("es-UY");
        }
        catch (CultureNotFoundException)
        {
            // Fallback manual: mismos separadores/símbolo/patrones que es-UY expone hoy
            // (verificado con CultureInfo.GetCultureInfo("es-UY").NumberFormat):
            // CurrencyPositivePattern=2 ("$ n"), CurrencyNegativePattern=9 ("-$ n").
            return new NumberFormatInfo
            {
                CurrencySymbol = "$",
                CurrencyDecimalDigits = 2,
                CurrencyDecimalSeparator = ",",
                CurrencyGroupSeparator = ".",
                CurrencyPositivePattern = 2,
                CurrencyNegativePattern = 9,
                NumberDecimalSeparator = ",",
                NumberGroupSeparator = ".",
            };
        }
    }
}
