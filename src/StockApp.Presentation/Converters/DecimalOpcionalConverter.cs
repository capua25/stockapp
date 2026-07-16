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
///
/// Cultura FIJA es-UY (NO se usa la <paramref name="culture"/> que pasa el binding): la app
/// no fija ningún <see cref="CultureInfo"/> global (mismo criterio que <see cref="MonedaConverter"/>
/// y <see cref="StockApp.Presentation.ViewModels.Finanzas.LineaPoaFormViewModel.CulturaMonto"/>),
/// así que depender de la cultura ambiente/del binding hacía que "850,50" se interpretara con
/// <see cref="CultureInfo.InvariantCulture"/> en máquinas no es-*, y <see cref="NumberStyles.Number"/>
/// (que incluye <see cref="NumberStyles.AllowThousands"/>) descartaba la coma como separador de
/// miles — bug real: "850,50" se guardó como 85050. Fix: cultura fija + <c>AllowDecimalPoint |
/// AllowLeadingSign</c> (SIN <c>AllowThousands</c>), igual que el fix de MontoTexto en
/// LineaPoaFormViewModel. Si "es-UY" no está disponible en el runtime se cae a un
/// <see cref="NumberFormatInfo"/> armado a mano con los mismos separadores.
/// </summary>
public sealed class DecimalOpcionalConverter : IValueConverter
{
    public static readonly DecimalOpcionalConverter Instance = new();

    private static readonly IFormatProvider CulturaFija = CrearCultura();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? d.ToString(CulturaFija) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var texto = value as string;
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        if (decimal.TryParse(
                texto,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CulturaFija,
                out var resultado))
            return resultado;

        return new BindingNotification(
            new FormatException("El valor ingresado no es un número válido."),
            BindingErrorType.Error);
    }

    private static IFormatProvider CrearCultura()
    {
        try
        {
            return CultureInfo.GetCultureInfo("es-UY");
        }
        catch (CultureNotFoundException)
        {
            return new NumberFormatInfo
            {
                NumberDecimalSeparator = ",",
                NumberGroupSeparator = ".",
            };
        }
    }
}
