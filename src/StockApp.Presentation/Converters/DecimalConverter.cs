using System;
using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte entre <c>decimal</c> (NO nullable) y el <c>string</c> que edita una celda de
/// <c>DataGridTextColumn</c> — variante de <see cref="DecimalOpcionalConverter"/> para campos que
/// siempre requieren un valor (ej. Cantidad/Precio unitario en la grilla de renglones de "Ingreso
/// de stock por factura").
///
/// Bug real (verificación visual, factura con precio "12,35"): las columnas "Cantidad" y "Precio
/// unitario" de <c>IngresoPorFacturaView.axaml</c> bindeaban DIRECTO a las propiedades
/// <c>decimal</c> del renglón, SIN converter. <c>DataGridBoundColumn.GenerateEditingElement</c>
/// arma la celda de edición con <c>Convert.ChangeType(value, typeof(decimal), culture)</c>
/// (verificado con ilspycmd contra Avalonia.Controls.DataGrid 12.0.1), que para string→decimal usa
/// <c>CultureInfo.CurrentCulture</c> (la cultura AMBIENTE del hilo — la app no fija ninguna, ver
/// <see cref="MonedaConverter"/>) y <c>NumberStyles.Number</c>, que INCLUYE
/// <c>AllowThousands</c>. En una máquina con cultura ambiente es-UY/es-AR, escribir "12.35"
/// hace que el punto se interprete como separador de miles: la grilla rechaza el valor con un
/// error de validación (o, peor, si la agrupación resulta "válida" por casualidad — ej.
/// "1.200" — el valor se guarda como 1200, cien veces más grande que lo tipeado, SIN aviso).
/// Verificado con un test headless que edita la celda real bajo cultura es-UY explícita
/// (ver IngresoPorFacturaLocaleDecimalTests.cs en StockApp.Presentation.UiTests).
///
/// IMPORTANTE — este bug es ESPECÍFICO de <c>DataGridTextColumn</c>, NO de cualquier binding
/// <c>decimal</c> sin converter. Un <c>TextBox</c> plano con <c>Text="{Binding AlgoDecimal}"</c>
/// FUERA de un <c>DataGrid</c> cae en <see cref="Avalonia.Data.Converters.DefaultValueConverter"/>
/// vía <c>TypeUtilities.TryConvert</c>, que para <c>decimal</c> usa <c>NumberStyles.Float</c>
/// (AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign | AllowDecimalPoint |
/// AllowExponent) — un estilo que NO incluye <c>AllowThousands</c>. Con cultura ambiente es-UY,
/// ese binding YA rechaza "12.35"/"1.200" sin corromper el valor (queda el valor viejo, con
/// <c>DataValidationErrors.HasErrors</c> en <c>True</c>) — este converter NO hace falta ahí y no
/// cambia el comportamiento si se agrega (confirmado con test A/B cableando y sacando el
/// converter sobre <c>MovimientoFormControl.axaml</c> — Cantidad, un <c>TextBox</c> plano —, ver
/// <c>MovimientoFormControlCantidadCulturaTests.cs</c> en StockApp.Presentation.UiTests). Si en el
/// futuro un campo <c>decimal</c> pasa de un <c>TextBox</c> plano a una celda de
/// <c>DataGridTextColumn</c>, AHÍ SÍ hay que cablear este converter — es el traspaso de codepath
/// (<c>TypeUtilities.TryConvert</c> → <c>Convert.ChangeType</c>) lo que reintroduce el bug, no el
/// tipo <c>decimal</c> en sí.
///
/// Mismo fix que <see cref="DecimalOpcionalConverter"/>: cultura FIJA es-UY (NO la cultura
/// ambiente del hilo/binding) + <c>NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign</c>
/// (SIN <c>AllowThousands</c>) — así "12,35" siempre parsea a 12.35 sin importar la máquina, y un
/// "12.35" tipeado por error nunca se cuela como miles. Si "es-UY" no está disponible en el
/// runtime se cae a un <see cref="NumberFormatInfo"/> armado a mano con los mismos separadores.
/// </summary>
public sealed class DecimalConverter : IValueConverter
{
    public static readonly DecimalConverter Instance = new();

    private static readonly IFormatProvider CulturaFija = CrearCultura();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? d.ToString(CulturaFija) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var texto = value as string;
        if (string.IsNullOrWhiteSpace(texto))
            return new BindingNotification(
                new FormatException("El valor ingresado no puede estar vacío."),
                BindingErrorType.Error);

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
